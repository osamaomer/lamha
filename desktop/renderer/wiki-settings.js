/* Lamha desktop — Settings → ويكيبيديا دون إنترنت (Offline Wikipedia). Injected into options.html by main.js, after the
 * Dictionary section, like the clipboard's settings; built from the page's own components (.panel, .opt, .packs).
 * The files, downloads and Kiwix's catalog live in the main process (wiki-library.js), reached through lamhaWiki. */
"use strict";
(async () => {
  if (document.getElementById("wikiPanel") || !document.getElementById("dictionary") || !window.lamhaWiki) return;
  await LamhaI18n.init();
  const { h, ask, icon } = LamhaClipList;
  const L = (key, vars) => LamhaI18n.t(key, vars);
  const num = (n, opts) => LamhaI18n.num(n, opts);
  const LANGS = ["ar", "en", "fr", "de", "es", "tr", "fa", "ur", "it", "pt", "ru", "zh", "ja", "ko", "hi", "id", "ms", "nl", "sv", "pl", "he"];
  const FLAVOURS = ["mini", "nopic", "maxi"];
  const cap = s => s.charAt(0).toUpperCase() + s.slice(1);
  const ui = () => (LamhaI18n.lang() === "en" ? "en-US" : "ar-EG");
  const langName = code => {
    const key = "lang." + code;
    const t = L(key);
    if (t !== key) return t;
    try { return new Intl.DisplayNames([ui()], { type: "language" }).of(code); } catch (_) { return code; }
  };
  const size = bytes => (bytes >= 1e9
    ? L("d.wGB", { n: num(bytes / 1e9, { maximumFractionDigits: 1 }) })
    : L("d.wMB", { n: num(Math.max(1, bytes / 1e6), { maximumFractionDigits: 0 }) }));
  const month = date => (/^\d{4}-\d{2}/.test(date || "") ? new Date(date.slice(0, 7) + "-15T12:00:00").toLocaleDateString(ui(), { year: "numeric", month: "long" }) : "");
  const scopeName = e => (e.scope === "top" ? L("d.wTop") : e.scope === "all" ? L("d.wAll") : e.title || e.scope);
  const flavourName = f => (FLAVOURS.includes(f) ? L("d.w" + cap(f)) : f || "");
  const fileName = f => [langName(f.lang), scopeName(f), flavourName(f.flavour)].filter(Boolean).join(" · ");
  const err = code => L("d.wErr_" + (["offline", "no_space", "checksum", "folder", "not_zim", "not_wikipedia", "too_old"].includes(code) ? code : "failed"));
  const sw = (id, label) => h("span", { class: "switch" }, h("input", { type: "checkbox", id, "aria-label": label }), h("span", { class: "track" }, h("span", { class: "thumb" })));

  const haveList = h("ul", { class: "packs wk-files", id: "wikiFiles", "aria-label": L("d.wHave") });
  const langSelect = h("select", { id: "wikiLang", "aria-label": L("d.wLang") }, LANGS.map(c => h("option", { value: c }, langName(c))));
  const catalogBox = h("div", { class: "wk-catalog", id: "wikiCatalog", "aria-live": "polite" });
  const folderPath = h("small", { class: "wk-path", dir: "ltr", id: "wikiFolder" });
  const folderDefault = h("button", { class: "btn small ghost", type: "button", id: "wikiFolderDefault" }, L("d.wDefault"));
  const addMsg = h("small", { class: "wk-msg", role: "status" });
  const getMsg = h("p", { class: "banner wk-err", role: "alert", hidden: true }); // a download that couldn't start
  const showError = code => { getMsg.textContent = err(code); getMsg.hidden = false; };

  const panel = h("section", { class: "panel", id: "wikiPanel" },
    h("h2", null, L("d.wTitle")),
    h("p", { class: "muted" }, L("d.wIntro")),
    h("h3", { class: "sub" }, L("d.wHave")),
    haveList,
    h("label", { class: "opt" },
      h("div", null, h("b", null, L("d.wFirst")), h("small", null, L("d.wFirstHint"))),
      sw("wikiOfflineFirst", L("d.wFirst"))),
    h("div", { class: "wk-get-h" }, h("h3", { class: "sub" }, L("d.wGet")), langSelect),
    getMsg,
    catalogBox,
    h("div", { class: "opt" },
      h("div", { class: "grow" }, h("b", null, L("d.wFolder")), folderPath),
      h("div", { class: "cb-set-acts" },
        h("button", { class: "btn small ghost", type: "button", id: "wikiFolderChange" }, L("d.wChange")),
        h("button", { class: "btn small ghost", type: "button", id: "wikiFolderOpen" }, L("d.wOpen")),
        folderDefault)),
    h("div", { class: "opt" },
      h("div", { class: "grow" }, h("b", null, L("d.wAdd")), h("small", null, L("d.wAddHint")), addMsg),
      h("button", { class: "btn small ghost", type: "button", id: "wikiAdd" }, L("d.wAddBtn"))),
    h("p", { class: "tip" }, L("d.wCredits"))
  );
  document.getElementById("dictionary").after(panel);
  const $ = id => document.getElementById(id);

  let state = { folder: "", isDefault: true, files: [], downloads: [] };
  let catalog = { lang: "", entries: null, error: "" };
  let topicsOpen = false;

  /* ---- what's on this PC, and what's downloading ---- */

  function progressText(d) {
    if (d.state === "checking") return L("d.wChecking", { p: num(d.size ? d.got / d.size : 0, { style: "percent" }) });
    if (d.state === "queued") return L("d.wQueued");
    const parts = [L("d.wProgress", { got: size(d.got), size: size(d.size) })];
    if (d.state === "paused") parts.push(L("d.wPaused"));
    if (d.state === "running" && d.speed > 0) {
      parts.push(L("d.wSpeed", { n: size(d.speed) }));
      const min = Math.ceil((d.size - d.got) / d.speed / 60);
      parts.push(min >= 60 ? L("d.wLeftHours", { h: Math.floor(min / 60), m: min % 60 }) : L("d.wLeftMin", { n: min }));
    }
    return parts.join(" · ");
  }

  function downloadRow(d) {
    const running = ["running", "queued", "checking"].includes(d.state);
    const bar = h("div", { class: "wk-bar", role: "progressbar", "aria-valuemin": "0", "aria-valuemax": "100", "aria-valuenow": String(Math.round(d.size ? (d.got / d.size) * 100 : 0)) },
      h("span", { style: `transform: scaleX(${d.size ? Math.min(1, d.got / d.size) : 0})` }));
    return h("li", { class: "wk-dl", "data-id": d.id },
      h("div", { class: "grow" },
        h("b", null, fileName(d)),
        h("small", { class: "wk-prog" }, progressText(d)),
        bar,
        d.state === "failed" && h("small", { class: "err" }, err(d.error))),
      h("div", { class: "cb-set-acts" },
        d.state !== "checking" && h("button", { class: "btn small ghost", type: "button", onclick: () => (running ? lamhaWiki.pause(d.id) : lamhaWiki.resume(d.id)) },
          running ? L("d.wPause") : d.state === "failed" ? L("d.wRetry") : L("d.wResume")),
        h("button", { class: "link", type: "button", onclick: () => lamhaWiki.cancel(d.id) }, L("d.cancel"))));
  }

  function fileRow(f) {
    const info = [L("d.wArticles", { n: f.articles }), size(f.size), month(f.date)].filter(Boolean).join(" · ");
    return h("li", { "data-id": f.id },
      h("div", { class: "grow" },
        h("b", null, fileName(f)),
        h("small", null, info),
        f.imported && h("small", { class: "wk-path", dir: "ltr", title: L("d.wImported") }, f.file),
        f.missing && h("small", { class: "err" }, L("d.wMissing"))),
      h("div", { class: "cb-set-acts" },
        !f.missing && h("button", { class: "btn small ghost", type: "button", onclick: () => lamhaWiki.openReader(f.id, "") }, L("d.wRead")),
        h("button", { class: "btn small danger", type: "button", onclick: () => removeFile(f) }, icon("trash"), L("common.remove"))));
  }

  async function removeFile(f) {
    const yes = f.imported
      ? await ask(L("d.wForgetQ", { name: fileName(f) }), L("d.wForgetQText"), [{ label: L("common.remove"), value: true, danger: true }, { label: L("d.cancel"), value: false }])
      : await ask(L("d.wRemoveQ", { name: fileName(f) }), L("d.wRemoveQText", { size: size(f.size) }), [{ label: L("d.delete"), value: true, danger: true }, { label: L("d.cancel"), value: false }]);
    if (yes) await lamhaWiki.remove(f.id);
  }

  /** Rows are rebuilt only when what's listed changes; while a download runs, its numbers and bar change in place
   *  (a row rebuilt under the pointer would swallow a click on Pause). */
  let haveShape = "";
  function renderHave() {
    const shape = JSON.stringify([state.downloads.map(d => [d.id, d.state, d.error]), state.files.map(f => [f.id, f.missing]), state.folder]);
    if (shape === haveShape) {
      for (const d of state.downloads) {
        const li = haveList.querySelector(`li[data-id="${CSS.escape(d.id)}"]`);
        if (!li) continue;
        li.querySelector(".wk-prog").textContent = progressText(d);
        const pct = d.size ? Math.min(1, d.got / d.size) : 0;
        li.querySelector(".wk-bar").setAttribute("aria-valuenow", String(Math.round(pct * 100)));
        li.querySelector(".wk-bar span").style.transform = `scaleX(${pct})`;
      }
      return;
    }
    haveShape = shape;
    const rows = [...state.downloads.map(downloadRow), ...state.files.map(fileRow)];
    haveList.replaceChildren(...(rows.length ? rows : [h("li", { class: "empty" }, L("d.wNone"))]));
    folderPath.textContent = state.folder;
    folderDefault.hidden = state.isDefault;
  }

  /* ---- Kiwix's catalog, one language at a time ---- */

  async function loadCatalog(lang) {
    catalog = { lang, entries: null, error: "" };
    renderCatalog();
    const r = await lamhaWiki.catalog(lang);
    if (catalog.lang !== lang) return; // another language was chosen meanwhile
    catalog = r.ok ? { lang, entries: r.data, error: "" } : { lang, entries: null, error: r.error || "failed" };
    renderCatalog();
  }

  function entryRow(e, suggested) {
    const same = f => f.name === e.name && f.flavour === e.flavour && String(f.date).slice(0, 7) === e.date.slice(0, 7); // a file added by hand
    const have = state.files.find(f => f.id === e.id || same(f)) || state.downloads.find(d => d.id === e.id);
    const older = !have && state.files.find(f => f.name === e.name && f.flavour === e.flavour && f.date < e.date);
    const hint = FLAVOURS.includes(e.flavour) ? L("d.w" + cap(e.flavour) + "Hint") : "";
    return h("li", { "data-id": e.id },
      h("div", { class: "grow" },
        h("b", null, [e.scope === "top" || e.scope === "all" ? scopeName(e) : e.title, flavourName(e.flavour)].filter(Boolean).join(" · "),
          suggested && h("span", { class: "wk-tag" }, L("d.wStart")),
          older && h("span", { class: "wk-tag" }, L("d.wNewer"))),
        h("small", null, [hint, L("d.wArticles", { n: e.articles }), size(e.size), month(e.date)].filter(Boolean).join(" · "))),
      have
        ? h("span", { class: "wk-have" }, L("d.wHaveIt"))
        : h("button", { class: "btn small ghost", type: "button", onclick: () => download(e) }, L("d.wGet")));
  }

  async function download(e) {
    if (e.size > 2e9 && !await ask(L("d.wBigQ", { size: size(e.size) }), L("d.wBigQText"), [{ label: L("d.wGet"), value: true }, { label: L("d.cancel"), value: false }])) return;
    getMsg.hidden = true;
    const r = await lamhaWiki.download(e.id);
    if (!r.ok) showError(r.error);
  }

  function renderCatalog() {
    if (!catalog.entries) {
      catalogBox.replaceChildren(catalog.error
        ? h("p", { class: "muted wk-state" }, L("d.wCatalogOffline"), " ", h("button", { class: "link", type: "button", onclick: () => loadCatalog(catalog.lang) }, L("d.wRetry")))
        : h("p", { class: "muted wk-state" }, L("d.wLoading")));
      return;
    }
    if (!catalog.entries.length) { catalogBox.replaceChildren(h("p", { class: "muted wk-state" }, L("d.wNoFiles"))); return; }
    const order = e => FLAVOURS.indexOf(e.flavour) + (FLAVOURS.includes(e.flavour) ? 0 : 9);
    const main = ["top", "all"].flatMap(scope => catalog.entries.filter(e => e.scope === scope).sort((a, b) => order(a) - order(b)));
    const topics = catalog.entries.filter(e => e.scope !== "top" && e.scope !== "all")
      .sort((a, b) => a.title.localeCompare(b.title) || order(a) - order(b));
    const nothingYet = !state.files.length && !state.downloads.length;
    catalogBox.replaceChildren(
      h("ul", { class: "packs" }, main.map((e, i) => entryRow(e, nothingYet && i === 0 && e.scope === "top"))),
      ...(topics.length ? [h("details", { class: "wk-topics", open: topicsOpen, ontoggle: e => { topicsOpen = e.currentTarget.open; } },
        h("summary", null, L("d.wTopics", { n: new Set(topics.map(e => e.name)).size })),
        h("ul", { class: "packs" }, topics.map(e => entryRow(e, false))))] : [])
    );
  }

  /* ---- load, and keep up with the main process ---- */

  async function refresh() {
    const r = await lamhaWiki.list();
    if (!r.ok) return;
    const before = owned();
    state = r.data;
    renderHave();
    if (catalog.entries && owned() !== before) renderCatalog(); // "Downloaded" marks follow
  }
  const owned = () => [...state.files.map(f => f.id), ...state.downloads.map(d => d.id)].join("|");

  let pending = false;
  lamhaWiki.onChanged(() => { // several times a second while a download runs: one redraw per frame at most
    if (pending) return;
    pending = true;
    requestAnimationFrame(() => { pending = false; refresh(); });
  });

  $("wikiOfflineFirst").addEventListener("change", e => browser.storage.local.set({ wikiOfflineFirst: e.target.checked }));
  langSelect.addEventListener("change", () => loadCatalog(langSelect.value));
  $("wikiFolderChange").addEventListener("click", async () => {
    getMsg.hidden = true;
    const r = await lamhaWiki.chooseFolder();
    if (!r.ok) showError(r.error);
  });
  $("wikiFolderOpen").addEventListener("click", () => lamhaWiki.openFolder());
  folderDefault.addEventListener("click", () => lamhaWiki.defaultFolder());
  $("wikiAdd").addEventListener("click", async () => {
    addMsg.textContent = "";
    addMsg.classList.remove("err");
    const r = await lamhaWiki.addFile();
    if (!r.ok) { addMsg.textContent = err(r.error); addMsg.classList.add("err"); }
    else if (r.data) addMsg.textContent = L("d.wAdded", { name: fileName(r.data) });
  });

  const [{ wikiOfflineFirst }, { targetLang }] = await Promise.all([
    browser.storage.local.get({ wikiOfflineFirst: false }), browser.storage.sync.get({ targetLang: "ar" })
  ]);
  $("wikiOfflineFirst").checked = !!wikiOfflineFirst;
  langSelect.value = LANGS.includes(targetLang) ? targetLang : "ar";
  await refresh();
  loadCatalog(langSelect.value);

  if (location.hash === "#wikipedia") panel.scrollIntoView({ block: "start" });
})();
