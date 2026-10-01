/* Lamha desktop — Settings → الحافظة and التحديثات. The settings window shows the extension's options.html, which
 * this app doesn't modify: main.js injects this script to add the sections, built from the page's own components
 * (.panel, .opt, .switch, select, .sites). Values live in storage.local (read by main.js). */
"use strict";
(async () => {
  if (document.getElementById("clipPanel") || !document.getElementById("review")) return;
  await LamhaI18n.init(); // options.js reloads the page when the language changes
  const { h, ask, arNum, appName } = LamhaClipList;
  const L = (key, vars) => LamhaI18n.t(key, vars);
  const DEFAULT_APPS = ["keepass.exe", "keepassxc.exe", "1password.exe", "bitwarden.exe", "enpass.exe", "dashlane.exe", "nordpass.exe"];
  const DEFAULTS = { clipboardEnabled: false, clipboardMaxItems: 500, clipboardExpiryDays: 30, clipboardSkipCards: true, clipboardExcludedApps: DEFAULT_APPS };
  const local = browser.storage.local;

  const sw = (id, label) => h("span", { class: "switch" }, h("input", { type: "checkbox", id, "aria-label": label }), h("span", { class: "track" }, h("span", { class: "thumb" })));
  const select = (id, label, options) => h("select", { id, "aria-label": label }, options.map(([v, t]) => h("option", { value: String(v) }, t)));

  const enc = h("p", { class: "muted", id: "cbEnc", role: "status" });
  const appsList = h("ul", { class: "sites", id: "cbApps", "aria-label": L("d.sApps") });
  const appInput = h("input", { type: "text", id: "cbAppInput", dir: "ltr", placeholder: "program.exe", "aria-label": L("d.sAppName"), autocomplete: "off" });
  const appMsg = h("span", { class: "cb-set-msg", role: "status" });
  const seen = h("div", { class: "cb-seen", id: "cbSeen" });

  const panel = h("section", { class: "panel", id: "clipPanel" },
    h("h2", null, L("d.sTitle")),
    h("p", { class: "muted" }, L("d.sIntroA"), h("span", { class: "combo" }, h("kbd", null, "Alt"), "+", h("kbd", null, "Shift"), "+", h("kbd", null, "V")),
      L("d.sIntroB")),
    enc,
    h("label", { class: "opt" },
      h("div", null, h("b", null, L("d.enable")), h("small", null, L("d.sEnableHint"))),
      sw("clipboardEnabled", L("d.enable"))),
    h("div", { class: "opt" },
      h("div", null, h("b", null, L("d.sMax")), h("small", null, L("d.sMaxHint"))),
      select("clipboardMaxItems", L("d.sMax"), [100, 250, 500, 1000].map(n => [n, arNum(n)]))),
    h("div", { class: "opt" },
      h("div", null, h("b", null, L("d.sExpiry")), h("small", null, L("d.sExpiryHint"))),
      select("clipboardExpiryDays", L("d.sExpiry"), [[1, L("d.sDay")], [7, L("d.s7")], [30, L("d.s30")], [90, L("d.s90")], [0, L("d.sNever")]])),
    h("label", { class: "opt" },
      h("div", null, h("b", null, L("d.sCards")), h("small", null, L("d.sCardsHint"))),
      sw("clipboardSkipCards", L("d.sCards"))),
    h("h3", { class: "sub" }, L("d.sApps")),
    h("p", { class: "muted" }, L("d.sAppsHint")),
    appsList,
    h("form", { class: "key-row", id: "cbAppForm" }, appInput, h("button", { class: "btn small", type: "submit" }, L("d.sAdd")), appMsg),
    seen,
    h("div", { class: "opt" },
      h("div", null, h("b", null, L("d.sClear")), h("small", null, L("d.sClearHint"))),
      h("div", { class: "cb-set-acts" },
        // both delete: they look like it (Settings' own clear buttons: danger tint, trash icon)
        h("button", { class: "btn small danger", type: "button", id: "cbClearUnpinned" }, LamhaClipList.icon("trash"), L("d.clearUnpinned")),
        h("button", { class: "btn small danger", type: "button", id: "cbClearAll" }, LamhaClipList.icon("trash"), L("d.sClearAll"))))
  );
  document.getElementById("review").before(panel);
  const $ = id => document.getElementById(id);

  async function load() {
    const s = await local.get(DEFAULTS);
    $("clipboardEnabled").checked = !!s.clipboardEnabled;
    $("clipboardMaxItems").value = String(s.clipboardMaxItems);
    $("clipboardExpiryDays").value = String(s.clipboardExpiryDays);
    $("clipboardSkipCards").checked = s.clipboardSkipCards !== false;
    renderApps(s.clipboardExcludedApps);
    const st = await lamhaClipboard.status();
    const encrypted = st.ok && st.data.encrypted;
    enc.className = encrypted ? "muted cb-enc" : "banner cb-enc";
    enc.textContent = encrypted ? L("d.sEncrypted") : L("d.sNotEncrypted");
    renderSeen(s.clipboardExcludedApps);
  }

  function renderApps(list) {
    if (!list.length) {
      appsList.replaceChildren(h("li", { class: "empty" }, L("d.sNoApps")));
      return;
    }
    appsList.replaceChildren(...list.map(exe => h("li", null,
      h("span", null, exe),
      h("button", { class: "link", type: "button", "aria-label": L("d.sRemoveApp", { app: exe }), onclick: async () => { await lamhaClipboard.includeApp(exe); load(); } }, L("common.remove")))));
  }

  /** Programs in the history that aren't excluded: one click adds them. */
  async function renderSeen(excluded) {
    const r = await lamhaClipboard.apps();
    const apps = (r.ok ? r.data : []).filter(a => a.app !== "lamha" && a.app !== "unknown" && !excluded.includes(a.app)).slice(0, 12);
    seen.replaceChildren(...(apps.length ? [
      h("span", { class: "muted" }, L("d.sFromHistory")),
      ...apps.map(a => h("button", { class: "lc-chip", type: "button", title: L("d.sItemCount", { n: a.count }), onclick: () => add(a.app) }, "+ " + appName(a.app)))
    ] : []));
  }

  async function add(exe) {
    const r = await lamhaClipboard.excludeApp(exe);
    appMsg.textContent = r.ok ? "" : L("d.sBadApp");
    if (!r.ok) return;
    appInput.value = "";
    await load();
    const has = await lamhaClipboard.apps();
    const count = ((has.ok ? has.data : []).find(a => a.app === r.data) || {}).count;
    if (count && await ask(L("d.sDeleteAppQ", { app: appName(r.data) }), L("d.sDeleteAppQText", { n: count }),
      [{ label: L("d.delete"), value: true, danger: true }, { label: L("d.keepThem"), value: false }])) {
      await lamhaClipboard.removeApp(r.data);
      load();
    }
  }

  $("clipboardEnabled").addEventListener("change", async e => {
    if (e.target.checked) { await lamhaClipboard.setEnabled(true); return; }
    const choice = await ask(L("d.sOffQ"), L("d.sOffQText"),
      [{ label: L("d.delete"), value: "delete", danger: true }, { label: L("d.sKeepIt"), value: "keep" }]);
    if (!choice) { e.target.checked = true; return; } // Esc: nothing changes
    await lamhaClipboard.setEnabled(false, choice === "delete");
  });
  for (const id of ["clipboardMaxItems", "clipboardExpiryDays"]) {
    $(id).addEventListener("change", e => local.set({ [id]: Number(e.target.value) }));
  }
  $("clipboardSkipCards").addEventListener("change", e => local.set({ clipboardSkipCards: e.target.checked }));
  $("cbAppForm").addEventListener("submit", e => { e.preventDefault(); if (appInput.value.trim()) add(appInput.value); });
  $("cbClearUnpinned").addEventListener("click", async () => {
    if (await ask(L("d.clearUnpinnedQ"), L("d.sClearUnpinnedText"), [{ label: L("d.clear"), value: true, danger: true }, { label: L("d.cancel"), value: false }])) {
      await lamhaClipboard.clear({ keepPinned: true });
      load();
    }
  });
  $("cbClearAll").addEventListener("click", async () => {
    if (await ask(L("d.sClearAllQ"), L("d.sClearAllText"), [{ label: L("d.sClearAll"), value: true, danger: true }, { label: L("d.cancel"), value: false }])) {
      await lamhaClipboard.clear({ keepPinned: false });
      load();
    }
  });
  browser.storage.onChanged.addListener((changes, area) => {
    if (area === "local" && Object.keys(changes).some(k => k in DEFAULTS)) load();
  });

  load();

  /* ---- المظهر → the graphics card: main.js reads useGpu once, at start, so a change waits for the next start ---- */
  if (window.lamhaApp && document.getElementById("appearance")) {
    const gpuPending = h("div", { class: "opt", id: "gpuPending", hidden: true },
      h("div", null, h("small", { role: "status" }, L("d.gpuPending"))),
      h("button", { class: "btn small", type: "button", id: "gpuRestart", onclick: () => lamhaApp.restart() }, L("d.upRestartNow")));
    document.getElementById("appearance").append(
      h("label", { class: "opt" },
        h("div", null, h("b", null, L("d.gpu")), h("small", null, L("d.gpuHint"))),
        sw("useGpu", L("d.gpu"))),
      gpuPending);
    const renderGpu = async () => {
      const [{ useGpu }, running] = await Promise.all([local.get({ useGpu: true }), lamhaApp.gpu()]);
      $("useGpu").checked = useGpu !== false;
      gpuPending.hidden = (useGpu !== false) === running;
    };
    $("useGpu").addEventListener("change", e => local.set({ useGpu: e.target.checked }).then(renderGpu));
    browser.storage.onChanged.addListener((changes, area) => { if (area === "local" && changes.useGpu) renderGpu(); });
    renderGpu();
  }

  /* ---- التحديثات: from the GitHub Releases (updater.js) ---- */
  if (window.lamhaUpdates) {
    const upStatus = h("span", { class: "cb-up-status", role: "status" });
    const upBtn = h("button", { class: "btn small", type: "button", id: "upCheck" }, L("d.upCheckNow"));
    const upNews = h("div", { class: "up-news", id: "upNews" });
    const upPanel = h("section", { class: "panel", id: "updatesPanel" },
      h("h2", null, L("d.upTitle")),
      h("p", { class: "muted", id: "upVersion" }),
      h("label", { class: "opt" },
        h("div", null, h("b", null, L("d.upAuto")), h("small", null, L("d.upAutoHint"))),
        sw("updatesAuto", L("d.upAuto"))),
      h("div", { class: "opt" },
        h("div", null, h("b", null, L("d.upCheckRow")), upStatus),
        upBtn),
      upNews,
      h("button", { class: "link", type: "button", id: "upReleases" }, L("d.upAllReleases"))
    );
    (document.getElementById("privacy") || panel).after(upPanel); // the last section, easy to find (only the credits follow)
    const STATUS = {
      checking: () => L("d.upSChecking"),
      downloading: st => L("d.upSDownloading", { v: st.version }),
      ready: st => L("d.upSReady", { v: st.version }),
      available: st => L("d.upSAvailable", { v: st.version }),
      latest: () => L("d.upSLatest"),
      error: () => L("d.upSError"),
      dev: () => L("d.upDevBody")
    };
    let releases = "", newsFor = "";
    /** What's new in the installed version (shared/changelog.js, shipped with the app: it works offline). Earlier
     *  versions are on GitHub (the link under it). A version missing from the changelog (a build from source) shows the newest. */
    const renderNews = current => {
      if (newsFor === current) return;
      newsFor = current;
      const log = typeof LamhaChangelog !== "undefined" ? LamhaChangelog : [];
      if (!log.length) return;
      const at = Math.max(0, log.findIndex(e => e.v === current));
      const ui = LamhaI18n.lang() === "en" ? 1 : 0;
      const date = d => new Date(d + "T12:00:00").toLocaleDateString(ui ? "en-US" : "ar-EG", { year: "numeric", month: "long", day: "numeric" });
      upNews.replaceChildren(
        h("h3", { class: "sub" }, L("d.upNewIn", { v: log[at].v }), h("span", { class: "up-date" }, date(log[at].date))),
        h("ul", { class: "up-notes" }, log[at].notes.map(n => h("li", null, n[ui])))
      );
    };
    const renderUpdates = async () => {
      const st = await lamhaUpdates.state();
      releases = st.releases;
      renderNews(st.current);
      $("upVersion").textContent = L("d.upCurrent", { v: st.current, portable: st.portable });
      upStatus.textContent = STATUS[st.status] ? STATUS[st.status](st) : "";
      upBtn.textContent = st.status === "ready" ? L("d.upRestartNow") : st.status === "available" ? L("d.upDownloadBtn") : L("d.upCheckNow");
      upBtn.disabled = ["checking", "downloading"].includes(st.status);
      const { updatesAuto } = await local.get({ updatesAuto: true });
      $("updatesAuto").checked = updatesAuto !== false;
    };
    upBtn.addEventListener("click", async () => {
      const st = await lamhaUpdates.state();
      if (["ready", "available"].includes(st.status)) lamhaUpdates.restart(); else { await lamhaUpdates.check(); renderUpdates(); }
    });
    $("updatesAuto").addEventListener("change", e => local.set({ updatesAuto: e.target.checked }));
    $("upReleases").addEventListener("click", () => browser.tabs.create({ url: releases }));
    lamhaUpdates.onChanged(renderUpdates);
    renderUpdates();
  }

  /* ---- Firefox: the extension's link to this app (native-bridge.js), before Updates ---- */
  if (window.lamhaFirefox) {
    const fxState = h("small", { id: "fxState", role: "status" });
    const fxPanel = h("section", { class: "panel", id: "firefoxPanel" },
      h("h2", null, L("d.fxTitle")),
      h("p", { class: "muted" }, L("d.fxIntro")),
      h("label", { class: "opt" },
        h("div", null, h("b", null, L("d.fxLink")), h("small", null, L("d.fxLinkHint")), fxState),
        sw("firefoxLink", L("d.fxLink"))));
    (document.getElementById("privacy") || panel).after(fxPanel); // Updates stays the last section
    const renderFx = async () => {
      const [{ firefoxLink }, st] = await Promise.all([local.get({ firefoxLink: true }), lamhaFirefox.status()]);
      $("firefoxLink").checked = firefoxLink !== false;
      fxState.textContent = firefoxLink === false ? "" : st.connected ? L("d.fxConnected") : L("d.fxWaiting");
    };
    $("firefoxLink").addEventListener("change", e => local.set({ firefoxLink: e.target.checked }));
    lamhaFirefox.onChanged(renderFx);
    browser.storage.onChanged.addListener((changes, area) => { if (area === "local" && changes.firefoxLink) renderFx(); });
    renderFx();
  }

  const toSection = () => {
    const target = { "#clipboard": panel, "#updates": document.getElementById("updatesPanel") }[location.hash];
    if (target) target.scrollIntoView({ block: "start" });
  };
  window.addEventListener("hashchange", toSection); // Settings already open: only the hash changes
  toSection();
})();
