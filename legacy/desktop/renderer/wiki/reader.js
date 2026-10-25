/* Lamha desktop — the Wikipedia reader: articles from the downloaded files (Settings → ويكيبيديا دون إنترنت), in
 * Lamha's own design. main.js injects the shared scripts, sanitize.js, this file, then the content scripts, so Lamha's
 * lookup works in articles as on any web page (select a word → the card).
 * Articles come from the main process (lamhaWiki.article) and are rebuilt by LamhaWikiSanitize: nothing from the
 * file runs, and every link click is handled here. Opened at an article with lamhaWiki.onOpen or ?file=&path=. */
"use strict";
(async () => {
  await LamhaI18n.init({ onChange: () => location.reload() });
  const L = (key, vars) => LamhaI18n.t(key, vars);
  const num = (n, opts) => LamhaI18n.num(n, opts);
  const ui = () => (LamhaI18n.lang() === "en" ? "en-US" : "ar-EG");
  const root = document.documentElement;
  root.lang = LamhaI18n.lang();
  root.dir = LamhaI18n.dir();

  /* ---- small helpers (no framework, like the rest of Lamha) ---- */

  function h(tag, props, ...kids) {
    const el = document.createElement(tag);
    if (props) for (const [k, v] of Object.entries(props)) {
      if (v == null || v === false) continue;
      if (k === "class") el.className = v;
      else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
      else el.setAttribute(k, v === true ? "" : v);
    }
    kids.flat(Infinity).forEach(c => c != null && c !== false && el.append(c));
    return el;
  }
  const ICONS = {
    back: ["M5 12h14", "M13 6l6 6-6 6"], // points right: "back" in Arabic; mirrored in English (.flip)
    forward: ["M19 12H5", "M11 6l-6 6 6 6"],
    search: ["M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z", "M20 20l-3.5-3.5"],
    list: ["M8 6h13", "M8 12h13", "M8 18h13", "M3 6h.01", "M3 12h.01", "M3 18h.01"],
    external: ["M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6", "M15 3h6v6", "M10 14 21 3"],
    shuffle: ["M16 3h5v5", "M4 20 21 3", "M21 16v5h-5", "M15 15l6 6", "M4 4l5 5"],
    home: ["M3 10.5 12 3l9 7.5", "M5 9.5V21h14V9.5", "M10 21v-6h4v6"],
    book: ["M4 19.5A2.5 2.5 0 0 1 6.5 17H20", "M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"],
    minus: ["M5 12h14"], plus: ["M12 5v14", "M5 12h14"]
  };
  function icon(name, size = 18) {
    const s = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    for (const [k, v] of Object.entries({ viewBox: "0 0 24 24", width: size, height: size, fill: "none", stroke: "currentColor", "stroke-width": 2, "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true" })) s.setAttribute(k, v);
    for (const d of ICONS[name]) {
      const p = document.createElementNS("http://www.w3.org/2000/svg", "path");
      p.setAttribute("d", d);
      s.append(p);
    }
    return s;
  }
  const langName = code => {
    const key = "lang." + code;
    const t = L(key);
    if (t !== key) return t;
    try { return new Intl.DisplayNames([ui()], { type: "language" }).of(code); } catch (_) { return code; }
  };
  const month = date => (/^\d{4}-\d{2}/.test(date || "") ? new Date(date.slice(0, 7) + "-15T12:00:00").toLocaleDateString(ui(), { year: "numeric", month: "long" }) : "");
  const scopeName = f => (f.scope === "top" ? L("d.wTop") : f.scope === "all" ? L("d.wAll") : f.title || f.scope);
  const FLAVOURS = { mini: "d.wMini", nopic: "d.wNopic", maxi: "d.wMaxi" };
  const fileName = f => [langName(f.lang), scopeName(f), Object.hasOwn(FLAVOURS, f.flavour) ? L(FLAVOURS[f.flavour]) : ""].filter(Boolean).join(" · ");
  const assetUrl = fileId => p => `lamha-wiki://zim/${encodeURIComponent(fileId)}/${String(p).split("/").map(encodeURIComponent).join("/")}`;
  const onlineUrl = (lang, path) => `https://${lang}.wikipedia.org/wiki/${String(path).split("/").map(encodeURIComponent).join("/")}`;
  const open = url => browser.tabs.create({ url }); // the default browser (main.js: https only)

  /* ---- the page ---- */

  const q = h("input", {
    id: "rdQ", type: "search", role: "combobox", "aria-autocomplete": "list", "aria-expanded": "false", "aria-controls": "rdSug",
    placeholder: L("d.rSearch"), "aria-label": L("d.rSearch"), autocomplete: "off", spellcheck: "false", dir: "auto"
  });
  const sug = h("ul", { id: "rdSug", class: "rd-sug", role: "listbox", hidden: true });
  const btn = (id, name, label, extra = "") => h("button", { id, class: "rd-icon " + extra, type: "button", title: label, "aria-label": label }, icon(name));
  const backBtn = btn("rdBack", "back", L("d.rBack"), "flip");
  const fwdBtn = btn("rdFwd", "forward", L("d.rForward"), "flip");
  const tocBtn = btn("rdTocBtn", "list", L("d.rContents"));
  const onlineBtn = btn("rdOnline", "external", L("d.rOnline"));
  const toc = h("nav", { id: "rdToc", class: "rd-toc", "aria-label": L("d.rContents") });
  const main = h("main", { id: "rdMain", class: "rd-main", tabindex: "-1" });
  const toast = h("div", { class: "rd-toast", role: "status", hidden: true });
  document.getElementById("app").append(
    h("header", { class: "rd-bar" },
      h("div", { class: "rd-nav" }, btn("rdHome", "home", L("d.rHome")), backBtn, fwdBtn),
      h("div", { class: "rd-search" }, h("span", { class: "rd-search-i" }, icon("search", 16)), q, sug),
      h("div", { class: "rd-tools" },
        tocBtn,
        h("button", { id: "rdSmaller", class: "rd-icon rd-size", type: "button", title: L("d.rSmaller"), "aria-label": L("d.rSmaller") }, "A", icon("minus", 11)),
        h("button", { id: "rdBigger", class: "rd-icon rd-size", type: "button", title: L("d.rBigger"), "aria-label": L("d.rBigger") }, "A", icon("plus", 11)),
        onlineBtn)),
    h("div", { class: "rd-page" }, toc, main),
    toast
  );
  const $ = id => document.getElementById(id);

  /* ---- text size (kept in this window's storage) ---- */

  const SIZES = [14, 15, 16, 17, 18, 20, 22];
  let size = 3;
  try {
    const s = localStorage.getItem("lamhaWikiSize");
    if (s !== null && Number(s) >= 0 && Number(s) < SIZES.length) size = Number(s);
  } catch (_) { /* the default */ }
  const applySize = () => {
    root.style.setProperty("--rd-size", SIZES[size] + "px");
    $("rdSmaller").disabled = size === 0;
    $("rdBigger").disabled = size === SIZES.length - 1;
    try { localStorage.setItem("lamhaWikiSize", String(size)); } catch (_) { /* this time only */ }
  };
  applySize();
  $("rdSmaller").addEventListener("click", () => { size = Math.max(0, size - 1); applySize(); });
  $("rdBigger").addEventListener("click", () => { size = Math.min(SIZES.length - 1, size + 1); applySize(); });

  /* ---- toast ---- */

  let toastTimer = null;
  function say(text, action) {
    clearTimeout(toastTimer);
    toast.replaceChildren(h("span", null, text), action && h("button", { class: "rd-toast-act", type: "button", onclick: () => { toast.hidden = true; action.run(); } }, action.label));
    toast.hidden = false;
    toastTimer = setTimeout(() => { toast.hidden = true; }, action ? 7000 : 3500);
  }

  /* ---- history: back and forward, with where each page was scrolled to ---- */

  const history = [];
  let at = -1, current = null, reqId = 0;
  const updateNav = () => {
    backBtn.disabled = at <= 0;
    fwdBtn.disabled = at >= history.length - 1;
    onlineBtn.disabled = !(current && current.url);
    tocBtn.hidden = !(current && current.headings.length);
  };
  function remember() { if (at >= 0) history[at].scroll = scrollY; }

  async function go(target, { push = true, scroll = 0 } = {}) {
    const token = ++reqId;
    closeSuggestions();
    if (target.home) { // the start page is a place in the history too
      if (push) remember(); // (back and forward remembered it already)
      if (push) { history.splice(at + 1); history.push({ home: true, scroll: 0 }); at = history.length - 1; }
      current = null;
      await renderHome();
      scrollTo(0, scroll);
      updateNav();
      return;
    }
    main.setAttribute("aria-busy", "true");
    const r = await lamhaWiki.article(target.fileId || "", target.path || "");
    if (token !== reqId) return;
    main.removeAttribute("aria-busy");
    if (!r.ok) { say(L("d.rBroken")); return; }
    if (!r.data) { // a link to an article this copy doesn't have (Top files hold the most-read ones only)
      const lang = (files.find(f => f.id === target.fileId) || (current || {})).lang;
      say(L("d.rNotHere"), lang && target.path ? { label: L("d.rOpenOnline"), run: () => open(onlineUrl(lang, target.path)) } : null);
      return;
    }
    if (push) {
      remember();
      history.splice(at + 1);
      history.push({ fileId: r.data.fileId, path: r.data.path, scroll: 0 });
      at = history.length - 1;
    }
    renderArticle(r.data);
    if (target.anchor) jump(target.anchor); else scrollTo(0, scroll);
    updateNav();
  }
  const step = n => {
    const next = history[at + n];
    if (!next) return;
    remember();
    at += n;
    go(next, { push: false, scroll: next.scroll });
  };
  backBtn.addEventListener("click", () => step(-1));
  fwdBtn.addEventListener("click", () => step(1));
  $("rdHome").addEventListener("click", () => go({ home: true }));
  onlineBtn.addEventListener("click", () => { if (current && current.url) open(current.url); });

  function jump(id) {
    const el = document.getElementById(id);
    if (!el) return;
    const details = el.closest("details");
    if (details) details.open = true;
    el.scrollIntoView({ block: "start" });
  }

  /* ---- an article ---- */

  /** A line in the interface's language inside an article in another: it keeps the article's edge (an English
   *  label under an Arabic title sits on the right, like the title), its words the interface's direction. */
  const uiLine = (cls, dir, ...kids) => h("p", { class: cls, dir }, h("span", { dir: root.dir }, ...kids));

  function renderArticle(a) {
    const { fragment, headings } = LamhaWikiSanitize.article(a.html, { path: a.path, assetUrl: assetUrl(a.fileId) });
    const dir = LamhaI18n.textDir(a.lang);
    const file = files.find(f => f.id === a.fileId);
    current = { ...a, headings, html: undefined };
    const body = h("div", { class: "rd-body" }, fragment);
    main.replaceChildren(h("article", { class: "rd-article", dir, lang: a.lang },
      h("h1", { class: "rd-title" }, a.title),
      uiLine("rd-meta", dir, icon("book", 14), [file ? fileName(file) : langName(a.lang), month(a.date)].filter(Boolean).join(" · ")),
      body,
      a.flavour === "mini" && uiLine("rd-note", dir, L("d.rMini"), " ",
        a.url && h("button", { class: "link", type: "button", onclick: () => open(a.url) }, L("d.rReadFull"))),
      uiLine("rd-credit", dir, L("d.rCredit", { date: month(a.date) }))));
    document.title = a.title + " — " + L("d.rTitle");
    renderToc(headings, dir);
  }

  // every click on a link in an article is ours: another article, a place in this one, or the web (never a navigation)
  main.addEventListener("click", e => {
    const a = e.target.closest("a");
    if (!a || !main.contains(a)) return;
    e.preventDefault();
    if (a.dataset.path !== undefined && current) go({ fileId: current.fileId, path: a.dataset.path, anchor: a.dataset.anchor });
    else if (a.dataset.anchor) jump(a.dataset.anchor);
    else if (a.dataset.ext) open(a.dataset.ext);
  });
  main.addEventListener("auxclick", e => { if (e.target.closest("a")) e.preventDefault(); });

  function renderToc(headings, dir) {
    toc.replaceChildren(...(headings.length ? [
      h("div", { class: "rd-toc-h" }, L("d.rContents")),
      h("ol", { dir }, headings.map(x => h("li", { class: "l" + x.level },
        h("a", { href: "#", onclick: e => { e.preventDefault(); toc.classList.remove("open"); jump(x.id); } }, x.text))))
    ] : []));
    toc.classList.remove("open");
  }
  tocBtn.addEventListener("click", () => toc.classList.toggle("open"));

  /* ---- the start page: search, a random article, the files ---- */

  let files = [];
  async function loadFiles() {
    const r = await lamhaWiki.list();
    files = r.ok ? r.data.files.filter(f => !f.missing) : [];
  }

  async function randomArticle(fileId = "") {
    const r = await lamhaWiki.random(fileId);
    if (r.ok && r.data) go(r.data); else say(L("d.rBroken"));
  }

  async function renderHome() {
    await loadFiles();
    toc.replaceChildren();
    document.title = L("d.rTitle");
    if (!files.length) {
      main.replaceChildren(h("section", { class: "rd-home" },
        h("div", { class: "rd-logo" }, icon("book", 28)),
        h("h1", null, L("d.rNoFiles")),
        h("p", { class: "muted" }, L("d.rNoFilesHint")),
        h("button", { class: "btn", type: "button", onclick: () => open(browser.runtime.getURL("options/options.html#wikipedia")) }, L("d.rToSettings"))));
      return;
    }
    main.replaceChildren(h("section", { class: "rd-home" },
      h("div", { class: "rd-logo" }, icon("book", 28)),
      h("h1", null, L("d.wTitle")),
      h("p", { class: "muted" }, L("d.rHomeHint")),
      h("div", { class: "rd-home-acts" },
        h("button", { class: "btn", type: "button", id: "rdRandom", onclick: () => randomArticle() }, icon("shuffle", 16), L("d.rRandom"))),
      h("ul", { class: "rd-files" }, files.map(f => h("li", null,
        h("div", null, h("b", null, fileName(f)), h("small", null, [L("d.wArticles", { n: f.articles }), month(f.date)].filter(Boolean).join(" · "))),
        h("div", { class: "rd-file-acts" },
          h("button", { class: "btn small ghost", type: "button", onclick: () => go({ fileId: f.id, path: "" }) }, L("d.rMainPage")),
          h("button", { class: "btn small ghost", type: "button", onclick: () => randomArticle(f.id) }, L("d.rRandom")))))),
      h("button", { class: "link rd-manage", type: "button", onclick: () => open(browser.runtime.getURL("options/options.html#wikipedia")) }, L("d.rManage"))));
  }

  /* ---- search: titles as you type ---- */

  let sugItems = [], sugAt = -1, sugToken = 0, sugTimer = null;
  function closeSuggestions() {
    sug.hidden = true;
    q.setAttribute("aria-expanded", "false");
    q.removeAttribute("aria-activedescendant");
    sugAt = -1;
  }
  function renderSuggestions(query) {
    const langs = new Set(sugItems.map(s => s.lang));
    sug.replaceChildren(...(sugItems.length ? sugItems.map((s, i) => h("li", {
      id: "rdSug" + i, role: "option", class: i === sugAt ? "on" : null, "aria-selected": String(i === sugAt), dir: LamhaI18n.textDir(s.lang), // an Arabic title on the right
      onmousedown: e => { e.preventDefault(); choose(i); }, onmousemove: () => mark(i)
    }, h("span", { dir: "auto" }, s.title), langs.size > 1 && h("small", null, langName(s.lang))))
      : [h("li", { class: "none" }, L("d.rNoResults", { q: query }))]));
    sug.hidden = false;
    q.setAttribute("aria-expanded", "true");
  }
  function mark(i) {
    sugAt = i;
    [...sug.children].forEach((li, j) => { li.classList.toggle("on", j === i); li.setAttribute("aria-selected", String(j === i)); });
    if (i >= 0) q.setAttribute("aria-activedescendant", "rdSug" + i); else q.removeAttribute("aria-activedescendant");
  }
  function choose(i) {
    const s = sugItems[i];
    if (!s) return;
    q.value = "";
    q.blur();
    go({ fileId: s.fileId, path: s.path });
  }
  q.addEventListener("input", () => {
    clearTimeout(sugTimer);
    const query = q.value.trim();
    if (!query) { closeSuggestions(); return; }
    sugTimer = setTimeout(async () => {
      const token = ++sugToken;
      const r = await lamhaWiki.suggest(query, current ? current.lang : "");
      if (token !== sugToken || q.value.trim() !== query) return;
      sugItems = r.ok ? r.data : [];
      sugAt = sugItems.length ? 0 : -1;
      renderSuggestions(query);
      mark(sugAt);
    }, 120);
  });
  q.addEventListener("keydown", e => {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      if (sug.hidden || !sugItems.length) return;
      e.preventDefault();
      mark((sugAt + (e.key === "ArrowDown" ? 1 : -1) + sugItems.length) % sugItems.length);
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (sugAt >= 0) choose(sugAt);
    } else if (e.key === "Escape") {
      closeSuggestions();
    }
  });
  q.addEventListener("blur", () => setTimeout(closeSuggestions, 100));

  /* ---- keys: Alt+← / Alt+→, Ctrl+L or / to search, Ctrl +/− for the text ---- */

  document.addEventListener("keydown", e => {
    const typing = e.target.closest && e.target.closest("input, textarea, [contenteditable]");
    if (e.altKey && (e.key === "ArrowLeft" || e.key === "ArrowRight")) {
      e.preventDefault();
      step((e.key === "ArrowLeft") === (root.dir === "ltr") ? -1 : 1); // the arrow that points "back" in this direction
    } else if ((e.ctrlKey && e.key.toLowerCase() === "l") || (e.key === "/" && !typing)) {
      e.preventDefault();
      q.focus();
      q.select();
    } else if (e.ctrlKey && (e.key === "=" || e.key === "+" || e.key === "-")) {
      e.preventDefault();
      $(e.key === "-" ? "rdSmaller" : "rdBigger").click();
    } else if (e.key === "Escape" && toc.classList.contains("open")) {
      toc.classList.remove("open");
    }
  });

  /* ---- opened from the card, Settings or the tray ---- */

  lamhaWiki.onOpen(msg => {
    if (msg && msg.nav) step(msg.nav === "back" ? -1 : 1); // the mouse's back and forward buttons
    else if (msg && msg.path) { q.value = ""; go({ fileId: msg.file || "", path: msg.path }); }
    else if (msg) go({ home: true });
  });
  lamhaWiki.onChanged(async () => { await loadFiles(); if (!current) renderHome(); });

  await loadFiles();
  const start = new URLSearchParams(location.search);
  if (start.get("path")) go({ fileId: start.get("file") || "", path: start.get("path") });
  else go({ home: true });
})();
