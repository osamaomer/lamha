"use strict";

const $ = id => document.getElementById(id);
const { t, num } = LamhaI18n;
const DEFAULTS = { enabled: true, targetLang: "ar", disabledSites: [] };
let settings = { ...DEFAULTS };
let tab = null, host = "", pageActive = false, pageSupported = false;

function h(tag, props, ...kids) {
  const el = document.createElement(tag);
  if (props) for (const [k, v] of Object.entries(props)) {
    if (v == null || v === false) continue;
    if (k === "class") el.className = v;
    else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
    else el.setAttribute(k, v);
  }
  kids.flat().forEach(c => c != null && c !== false && el.append(c));
  return el;
}

async function init() {
  await Promise.all([LamhaI18n.init({ onChange: () => location.reload() }), LamhaMotion.ready]); // redrawn in the new language
  LamhaMotion.attach(document.documentElement);
  LamhaI18n.applyDom(document);
  settings = { ...DEFAULTS, ...(await browser.storage.sync.get(DEFAULTS)) };
  $("enabled").checked = settings.enabled;

  [tab] = await browser.tabs.query({ active: true, currentWindow: true });
  try {
    const url = new URL(tab.url);
    pageSupported = /^https?:$|^file:$/.test(url.protocol) && !/^(addons\.mozilla\.org|accounts-static\.cdn\.mozilla\.net)$/.test(url.hostname);
    host = url.hostname || url.protocol.replace(":", "");
  } catch (_) { pageSupported = false; }
  $("host").textContent = host || "—";
  $("siteEnabled").checked = !settings.disabledSites.includes(host);
  $("siteEnabled").disabled = !pageSupported;

  if (pageSupported) {
    try {
      const st = await browser.tabs.sendMessage(tab.id, { type: "pageState" }, { frameId: 0 });
      pageActive = !!(st && st.active);
    } catch (_) {
      pageSupported = false; // content script not present (e.g. tab opened before install)
      $("pageNote").textContent = t("p.reloadNote");
      $("pageNote").hidden = false;
    }
  } else {
    $("pageNote").textContent = t("p.specialPage");
    $("pageNote").hidden = false;
  }
  refreshSiteDot();
  refreshPageBtn();

  const hasPerm = await browser.permissions.contains({ origins: ["<all_urls>"] });
  $("perm").hidden = hasPerm;

  renderHistory();
  renderToday();
  await initCompose();
  initTabMotion();
}

/**
 * The mode tabs: a highlight that slides to the selected tab, and the new pane coming in from that side (mirrored in
 * Arabic). Watches aria-selected, so the desktop app's الحافظة tab (added by its own script) moves the same way.
 */
function initTabMotion() {
  const tabs = document.querySelector(".tabs");
  if (!tabs || typeof MutationObserver !== "function") return;
  const ink = h("span", { class: "tab-ink", "aria-hidden": "true" });
  tabs.prepend(ink);
  tabs.classList.add("has-ink");
  let current = null;
  const place = animate => {
    const sel = tabs.querySelector('[role="tab"][aria-selected="true"]');
    if (!sel) return;
    if (!animate) ink.style.transition = "none";
    Object.assign(ink.style, { width: sel.offsetWidth + "px", transform: `translateX(${sel.offsetLeft}px)` });
    if (!animate) { void ink.offsetWidth; ink.style.transition = ""; }
    if (animate && current && current !== sel) {
      const all = [...tabs.querySelectorAll('[role="tab"]')];
      const forward = all.indexOf(sel) > all.indexOf(current);
      const rtl = getComputedStyle(tabs).direction === "rtl";
      const pane = document.getElementById(sel.getAttribute("aria-controls"));
      const dx = (forward ? 1 : -1) * (rtl ? -1 : 1) * 16;
      LamhaMotion.play(pane, [{ opacity: 0, transform: `translateX(${dx}px)` }, { opacity: 1, transform: "none" }], { duration: 220 });
    }
    current = sel;
  };
  new MutationObserver(() => place(true)).observe(tabs, { subtree: true, childList: true, attributes: true, attributeFilter: ["aria-selected"] });
  if (typeof ResizeObserver === "function") new ResizeObserver(() => place(false)).observe(tabs);
  place(false);
}

function refreshSiteDot() {
  document.querySelector(".site-dot").classList.toggle("off", !settings.enabled || !$("siteEnabled").checked);
}

function refreshPageBtn() {
  const btn = $("pageBtn");
  btn.disabled = !pageSupported;
  btn.classList.toggle("stop", pageActive);
  $("pageLabel").textContent = pageActive ? t("p.stopPage") : t("p.translatePage");
}

/* ---- events ---- */

$("enabled").addEventListener("change", e => {
  settings.enabled = e.target.checked;
  browser.storage.sync.set({ enabled: settings.enabled });
  refreshSiteDot();
});

$("siteEnabled").addEventListener("change", e => {
  const set = new Set(settings.disabledSites);
  e.target.checked ? set.delete(host) : set.add(host);
  settings.disabledSites = [...set];
  browser.storage.sync.set({ disabledSites: settings.disabledSites });
  refreshSiteDot();
});

$("pageBtn").addEventListener("click", async () => {
  pageActive = !pageActive;
  await browser.tabs.sendMessage(tab.id, { type: "pageAction", action: pageActive ? "start" : "stop" }).catch(() => {});
  refreshPageBtn();
  if (pageActive) window.close();
});

$("grant").addEventListener("click", async () => {
  const ok = await browser.permissions.request({ origins: ["<all_urls>"] });
  if (ok) $("perm").hidden = true;
});

$("openOptions").addEventListener("click", () => { browser.runtime.openOptionsPage(); window.close(); });

/* ---- quick translate ---- */

let qTimer, qToken = 0;
const q = $("q");
q.addEventListener("input", () => {
  q.style.height = "auto";
  q.style.height = Math.min(140, q.scrollHeight) + "px";
  $("clearQ").hidden = !q.value;
  refreshHistVisibility(); // the Today card makes room for a result
  clearTimeout(qTimer);
  qTimer = setTimeout(runQuick, 420);
});
q.addEventListener("keydown", e => {
  if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); clearTimeout(qTimer); runQuick(); }
});
$("clearQ").addEventListener("click", () => {
  q.value = ""; q.dispatchEvent(new Event("input")); $("result").hidden = true; q.focus();
});

const dirOf = LamhaI18n.textDir;

async function runQuick() {
  const text = q.value.trim();
  const out = $("result");
  if (!text) { out.hidden = true; return; }
  const token = ++qToken;
  out.hidden = false;
  out.replaceChildren(h("div", { class: "loading" }, t("common.translating")));
  const res = await browser.runtime.sendMessage({ type: "lookup", text }).catch(() => null);
  if (token !== qToken) return;
  if (!res || !res.ok) {
    const msg = res && res.error === "rate_limited" ? t("p.busy")
      : res && res.error === "too_long" ? LamhaAI.errorInfo("ai_too_long").join(" — ")
      : t("p.failed");
    out.replaceChildren(h("div", { class: "error" }, msg));
    return;
  }
  const d = res.data;
  // an explained word with nothing to explain it (no definitions, no AI): say so rather than show an empty line
  const langOf = code => (Object.hasOwn({ ar: 1, en: 1, fr: 1, tr: 1, ur: 1, fa: 1, es: 1, de: 1 }, code) ? t("lang." + code) : code);
  const main = d.translation || (d.mode === "explain" ? t(d.explainMissing ? "c.noExplainAI" : "c.noDef", { lang: langOf(d.src) }) : "");
  const kids = [
    h("div", { class: "main" },
      h("div", { style: "flex:1;min-width:0" },
        h("div", { class: "tr" + (main.length > 40 ? " long" : ""), dir: dirOf(d.tl) }, main),
        d.type === "word" && d.srcTranslit && h("div", { class: "phon" }, `${d.query} · /${d.srcTranslit}/`)
      ),
      h("button", { class: "icon-btn", title: t("common.copy"), "aria-label": t("common.copy"), onclick: e => copyFrom(e.currentTarget, d.translation) }, copyIcon())
    )
  ];
  if (d.goal) kids.unshift(h("div", { class: "milestone" }, t("td.goalDone", { n: d.goal })));
  if (d.milestone) kids.unshift(h("div", { class: "milestone" }, t("ms.lookups", { n: d.milestone })));
  (d.dict || []).slice(0, 3).forEach(p => kids.push(
    h("div", { class: "pos-row" }, h("span", { class: "pos" }, p.pos), h("span", { class: "terms" }, p.terms.slice(0, 6).map(term => term.word).join(dirOf(d.tl) === "rtl" ? "، " : ", ")))
  ));
  const firstDef = d.definitions && d.definitions[0] && d.definitions[0].entries[0];
  if (firstDef && d.mode !== "explain") kids.push( // explained words: the definition is already the main line
    h("div", { class: "def" }, firstDef.glossTr && h("div", null, firstDef.glossTr), h("div", { class: "en" }, firstDef.gloss)));
  if (d.mode === "explain" && d.ar) kids.push(h("div", { class: "def" }, h("div", { dir: dirOf(d.other || "ar") }, d.ar))); // …and the translation under it
  out.replaceChildren(...kids);
  LamhaMotion.stagger([...out.children].filter(k => !k.classList.contains("main")), { each: 45, distance: 4 });
  if (d.milestone || d.goal) LamhaMotion.burst(out.querySelector(".milestone"));
  renderHistory();
}

/** Copies `text`; the button's icon turns into a check mark for a moment, so it's clear it worked. */
async function copyFrom(btn, text) {
  try { await navigator.clipboard.writeText(text); } catch (_) { flash(t("common.copyFailed")); return; }
  const svg = btn.querySelector("svg");
  if (!svg) return;
  const check = lineIcon(["M20 6 9 17l-5-5"], 15);
  svg.replaceWith(check);
  btn.classList.add("copied");
  LamhaMotion.play(check, [{ transform: "scale(.4)", opacity: 0 }, { transform: "none", opacity: 1 }], { duration: 220, easing: "cubic-bezier(.2,.9,.3,1.3)" });
  setTimeout(() => { if (check.isConnected) check.replaceWith(svg); btn.classList.remove("copied"); }, 1300);
}

function lineIcon(paths, size) {
  const NS = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(NS, "svg");
  Object.entries({ viewBox: "0 0 24 24", width: size, height: size, fill: "none", stroke: "currentColor", "stroke-width": 2.4, "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true" }).forEach(([k, v]) => svg.setAttribute(k, v));
  paths.forEach(d => { const p = document.createElementNS(NS, "path"); p.setAttribute("d", d); svg.append(p); });
  return svg;
}

function copyIcon() {
  const NS = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(NS, "svg");
  Object.entries({ viewBox: "0 0 24 24", width: 15, height: 15, fill: "none", stroke: "currentColor", "stroke-width": 2, "stroke-linecap": "round", "stroke-linejoin": "round" }).forEach(([k, v]) => svg.setAttribute(k, v));
  ["M20 9h-9a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2z", "M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"].forEach(d => {
    const p = document.createElementNS(NS, "path"); p.setAttribute("d", d); svg.append(p);
  });
  return svg;
}

/* ---- compose: writing tools on text typed here (Claude or Ollama) ---- */

const WRITE_TOOLS = [
  ["proofread", "tool.proofread"], ["improve", "tool.improveShort"], ["formal", "tool.formal"],
  ["friendly", "tool.friendly"], ["concise", "tool.concise"], ["expand", "tool.expand"], ["toEnglish", "tool.toEnglishShort"]
];
const draft = $("draft");
let aiLocal = {}, wrToken = 0, draftTimer;

const aiReady = () => LamhaAI.provider(aiLocal).ready;
const providerName = () => LamhaAI.provider(aiLocal).name;

async function initCompose() {
  const st = await browser.storage.local.get([...LamhaAI.PROVIDER_KEYS, "draft", "popupMode"]);
  aiLocal = st;
  draft.value = st.draft || "";
  renderTools();
  renderWeak();
  setMode(["write", "review"].includes(st.popupMode) ? st.popupMode : "translate");
  if (st.popupMode !== "review") loadReview(false); // just the tab's count
}

const MODES = { translate: ["tabTr", "trPane"], write: ["tabWr", "wrPane"], review: ["tabRv", "rvPane"] };
let mode = "translate";

function setMode(m) {
  mode = m;
  for (const [key, [tabId, paneId]] of Object.entries(MODES)) {
    $(tabId).setAttribute("aria-selected", String(key === m));
    $(paneId).hidden = key !== m;
  }
  if (m === "write") showSetup();
  browser.storage.local.set({ popupMode: m });
  refreshHistVisibility();
  if (m === "review") loadReview(true);
  else (m === "write" ? draft : q).focus();
}
$("tabTr").addEventListener("click", () => setMode("translate"));
$("tabWr").addEventListener("click", () => setMode("write"));
$("tabRv").addEventListener("click", () => setMode("review"));

/** Arabic drafts get "to English"; English drafts get the editing tools. */
function renderTools(active) {
  const arabic = LamhaAI.isArabicText(draft.value);
  const tools = WRITE_TOOLS.filter(([id]) => (arabic ? id === "toEnglish" : id !== "toEnglish"));
  $("wrTools").replaceChildren(
    ...tools.map(([id, label]) => h("button", {
      class: id === active ? "on" : null, disabled: !draft.value.trim() || !aiReady() || null, onclick: () => runWrite(id)
    }, t(label))),
    h("span", { class: "hint-key" }, h("span", { class: "combo" }, "Ctrl+Enter"))
  );
}

draft.addEventListener("input", () => {
  renderTools();
  clearTimeout(draftTimer); // keep the draft if the popup closes
  draftTimer = setTimeout(() => browser.storage.local.set({ draft: draft.value }), 300);
});
draft.addEventListener("keydown", e => {
  if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
    e.preventDefault();
    runWrite(LamhaAI.isArabicText(draft.value) ? "toEnglish" : "proofread");
  }
});

/** No AI chosen yet: say how to set one up as soon as the tab opens — a first step, not an error. */
function showSetup() {
  const out = $("wrOut");
  if (!aiReady()) { out.hidden = false; out.replaceChildren(wrError(LamhaAI.provider(aiLocal).notReady)); }
  else if (out.querySelector(".setup")) out.hidden = true;
}

function wrError(code, retry) {
  const [title, text, needsSettings] = LamhaAI.errorInfo(code, providerName());
  const setup = ["ai_no_key", "gemini_no_key", "ollama_no_model"].includes(code); // nothing chosen yet: neutral, not red
  return h("div", { class: setup ? "setup" : "error" }, h("b", null, title), text,
    h("div", { class: "acts" }, needsSettings
      ? h("button", { class: "btn small", onclick: openAISettings }, t("common.settings"))
      : retry && h("button", { class: "btn small", onclick: retry }, t("common.retry"))));
}

function openAISettings() {
  browser.tabs.create({ url: browser.runtime.getURL("options/options.html#ai") });
  window.close();
}

async function runWrite(tool, fresh = false) {
  const text = draft.value.trim();
  if (!text) return;
  const out = $("wrOut");
  out.hidden = false;
  if (!aiReady()) { out.replaceChildren(wrError(LamhaAI.provider(aiLocal).notReady)); return; }
  const token = ++wrToken;
  renderTools(tool);
  out.replaceChildren(h("div", { class: "loading" }, h("span", { class: "spin" }), t("common.working")));
  const logo = document.querySelector(".logo");
  logo.classList.add("thinking"); // the Lamha logo blinks while the AI works (full animations)
  const res = await browser.runtime.sendMessage({ type: "ai", tool, text, extra: fresh ? { fresh: true } : {} }).catch(e => ({ ok: false, error: String(e) }));
  if (token !== wrToken) return;
  logo.classList.remove("thinking");
  if (!res || !res.ok) { out.replaceChildren(wrError(res && res.error, () => runWrite(tool))); return; }

  const useIt = result => h("button", {
    class: "btn small", title: t("write.useItTitle"),
    onclick: () => { draft.value = result; draft.dispatchEvent(new Event("input")); out.hidden = true; draft.focus(); }
  }, t("write.useIt"));
  const actions = result => h("div", { class: "acts" },
    useIt(result),
    h("button", { class: "btn small ghost", onclick: () => navigator.clipboard.writeText(result).then(() => flash(t("common.copied"))) }, t("common.copy")),
    h("button", { class: "btn small ghost", title: t("write.again"), onclick: () => runWrite(tool, true) }, "↻")
  );

  if (tool !== "proofread") {
    const result = res.data.text || "";
    const box = h("div", { class: "text", dir: "auto" }, result);
    out.replaceChildren(box, actions(result));
    LamhaMotion.typeIn(box);
    return;
  }
  const { corrected = "", issues = [] } = res.data;
  if (!issues.length || corrected.trim() === text) {
    const ok = h("div", { class: "ok" }, t("write.noErrorsCheck"));
    out.replaceChildren(ok);
    LamhaMotion.burst(ok, { count: 10, glyphs: ["✓", "✦", "•"] });
    return;
  }
  const fixed = corrected.trim();
  const diff = h("div", { class: "text", dir: "ltr" }, LamhaAI.diffNodes(h, text, fixed));
  LamhaMotion.sequence(diff, "del, ins"); // each mistake struck through, then its fix
  out.replaceChildren(
    diff,
    actions(fixed),
    h("ul", { class: "issues" }, issues.map(i => h("li", null,
      h("div", { class: "fix" }, h("del", null, i.original), " → ", h("ins", null, i.fix)),
      i.category !== "other" && h("span", { class: "cat" }, LamhaAI.catLabel(i.category)),
      i.why && h("div", { class: "why" }, i.why)
    )))
  );
}

/** A short message at the bottom; with `action` ({ label, run }) it stays 5 s and offers that button (e.g. Undo). */
function flash(text, action) {
  document.querySelectorAll(".flash").forEach(f => f.remove());
  const ms = action ? 5000 : 1200;
  const t = h("div", { class: "flash", role: "status" }, text,
    action && h("button", { class: "flash-act", type: "button", onclick: () => { t.remove(); action.run(); } }, action.label),
    action && h("span", { class: "flash-bar", "aria-hidden": "true", style: `--undo:${ms}ms` })); // the time left to undo
  document.body.append(t);
  setTimeout(() => {
    if (!t.isConnected) return;
    LamhaMotion.exit(t, [{ opacity: 1 }, { opacity: 0, transform: "translate(-50%, 6px)" }], { duration: 140 }).then(() => t.remove());
  }, ms);
}

/** One line under the compose box: the user's most frequent mistake type → the journal. */
async function renderWeak() {
  const { mistakes } = await browser.storage.local.get("mistakes");
  const top = Object.entries((mistakes && mistakes.counts) || {})
    .filter(([c]) => c !== "other" && LamhaAI.isCategory(c))
    .sort((a, b) => b[1] - a[1])[0];
  const btn = $("weak");
  btn.hidden = !top;
  if (!top) return;
  btn.replaceChildren(t("write.topMistake"), h("b", null, LamhaAI.catLabel(top[0])), t("write.learnRule", { n: top[1] }));
  btn.onclick = () => { browser.tabs.create({ url: browser.runtime.getURL("options/options.html#journal") }); window.close(); };
}

browser.storage.onChanged.addListener((changes, area) => {
  if ((area === "local" && (changes.activity || changes.wotd)) || (area === "sync" && changes.dailyGoal)) renderToday();
  if (area === "local" && changes.history) renderHistory(); // words looked up on the card while this window is open (desktop)
  if (area !== "local") return;
  const ai = LamhaAI.PROVIDER_KEYS.filter(k => changes[k]);
  ai.forEach(k => { aiLocal[k] = changes[k].newValue; });
  if (ai.length) { renderTools(); if (mode === "write") showSetup(); } // set up in Settings meanwhile: the tools wake up
  if (changes.mistakes) renderWeak();
});

/* ---- review: flashcards of looked-up words (scheduling lives in the background) ---- */

let rv = { queue: [], counts: null, nextDue: 0 }, rvShown = false, rvEnter = false, rvHad = false;
const arNum = n => num(n);

/** "١٠ د" / "ساعة" / "يومان" / "٥ أيام" … or "10 min" / "1 hour" / "5 days" … until the next review. */
function spanLabel(ms) {
  const min = Math.round(ms / 60e3);
  if (min < 60) return t("span.min", { n: Math.max(1, min) });
  const hours = Math.round(min / 60);
  if (hours < 24) return t("span.hours", { n: hours });
  const days = Math.round(hours / 24);
  if (days < 30) return t("span.days", { n: days });
  const months = Math.round(days / 30);
  if (months < 12) return t("span.months", { n: months });
  return t("span.years", { n: Math.round(days / 365) });
}

/** Answers since the popup opened: the progress bar and the summary at the end. */
const rvSession = { n: 0, good: 0, hard: 0, again: 0 };

async function loadReview(render = true) {
  const res = await browser.runtime.sendMessage({ type: "reviewQueue" }).catch(() => null);
  if (!res) return;
  rv = res;
  const n = rv.counts.due + rv.counts.fresh;
  $("rvBadge").hidden = !n;
  $("rvBadge").textContent = arNum(n);
  if (render) { rvShown = false; renderReview(); }
}

function renderReview() {
  const { queue, counts } = rv;
  const c = queue[0];
  $("rvHead").replaceChildren(
    h("span", { class: "pill due" + (counts.due ? "" : " zero") }, t("rv.due", { n: counts.due })),
    h("span", { class: "pill new" + (counts.fresh ? "" : " zero") }, t("rv.fresh", { n: counts.fresh })),
    h("span", { class: "grow" }),
    h("span", null, t("rv.totals", counts))
  );
  // this sitting: how far through the cards waiting now (answers so far / answers + cards left)
  const all = rvSession.n + queue.length;
  const bar = $("rvProgress");
  bar.hidden = !rvSession.n || !queue.length;
  bar.setAttribute("aria-valuenow", String(Math.round((100 * rvSession.n) / (all || 1))));
  bar.firstElementChild.style.transform = `scaleX(${rvSession.n / (all || 1)})`;
  const enter = rvEnter;
  rvEnter = false;
  if (!c) {
    const done = counts.total
      ? h("div", { class: "rv-done" }, h("b", null, t("rv.doneTitle")),
        rvSession.n > 0 && h("div", { class: "rv-sum" }, t("rv.session", rvSession)), // what this sitting did
        rv.nextDue ? t("rv.next", { span: spanLabel(rv.nextDue - Date.now()) }) : "",
        rv.streak >= 2 && h("div", null, h("span", { class: "rv-streak" }, t("rv.streak", { n: rv.streak }))))
      : h("div", { class: "rv-done" }, h("b", null, t("rv.emptyTitle")), t("rv.emptyText"));
    $("rvCard").replaceChildren(done);
    if (enter && rvHad && counts.total) { // the last card was just answered: a small celebration
      LamhaMotion.play(done, [{ opacity: 0, transform: "scale(.94)" }, { opacity: 1, transform: "none" }], { duration: 300, easing: "cubic-bezier(.2,.9,.3,1.25)" });
      LamhaMotion.burst(done.querySelector("b"), { count: 18 });
    }
    rvHad = false;
    return;
  }
  rvHad = true;

  const front = h("div", { class: "rv-front" },
    c.isNew && h("div", { class: "rv-tag" }, t("rv.newWord")),
    h("div", { class: "rv-word-row" },
      h("div", { class: "rv-word" }, c.q),
      h("button", { class: "icon-btn", title: t("common.listen"), "aria-label": t("common.listen"), onclick: e => speakWord(e.currentTarget, c.q) }, speakIcon())),
    c.ex && h("div", { class: "rv-ex" }, highlight(c.ex, c.form || c.q))
  );
  const kids = [front];
  if (!rvShown) {
    kids.push(h("button", { class: "btn block rv-show", onclick: reveal }, t("rv.show"), h("kbd", null, "Space")));
  } else {
    kids.push(
      c.en // looked up in the English–English dictionary: its definition first, the meaning under it
        ? h("div", { class: "rv-back" }, h("div", { class: "rv-tr en", dir: "ltr" }, c.def), c.tr && h("div", { class: "rv-def" }, c.tr))
        : h("div", { class: "rv-back" }, h("div", { class: "rv-tr" }, c.tr), c.def && h("div", { class: "rv-def" }, c.def)),
      h("div", { class: "rv-grades" },
        gradeBtn("again", t("rv.again"), c.next.again, "1"),
        gradeBtn("hard", t("rv.hard"), c.next.hard, "2"),
        gradeBtn("good", t("rv.good"), c.next.good, "3")),
      h("div", { class: "rv-tools" },
        h("button", { class: "link", onclick: () => removeCurrent(c) }, t("rv.remove")))
    );
  }
  $("rvCard").replaceChildren(...kids);
  // the next card rises once the answered one is on its way
  if (enter) LamhaMotion.play($("rvCard"), [{ opacity: 0, transform: "translateY(14px) scale(.98)" }, { opacity: 1, transform: "none" }], { duration: 240, delay: LamhaMotion.full() ? 110 : 0 });
}

/**
 * The answered card leaves the way it was graded: "again" back toward the start of the line, "good" on toward its
 * end, "hard" drops a little. A copy of the card animates while the next one is already in place (full only).
 */
function flyOff(grade) {
  if (!LamhaMotion.full()) return;
  const front = $("rvCard").querySelector(".rv-front");
  if (!front) return;
  const box = $("rvCard"), r = box.getBoundingClientRect();
  // the word and its meaning fly; the buttons stay behind
  const ghost = h("div", { class: "rv-card rv-ghost", "aria-hidden": "true" }, front.cloneNode(true), ...[...box.querySelectorAll(".rv-back")].map(b => b.cloneNode(true)));
  Object.assign(ghost.style, {
    position: "fixed", left: r.left + "px", top: r.top + "px", width: r.width + "px", minHeight: "0",
    margin: "0", pointerEvents: "none", zIndex: "20", background: "var(--surface)", borderRadius: "12px", boxShadow: "0 12px 30px -10px rgba(0,0,0,.35)"
  });
  document.body.append(ghost);
  const end = getComputedStyle(box).direction === "rtl" ? -1 : 1; // toward the end of the line
  const dx = grade === "good" ? 170 * end : grade === "again" ? -170 * end : 0;
  const to = grade === "hard" ? "translateY(46px) scale(.92)" : `translateX(${dx}px) rotate(${dx > 0 ? 9 : -9}deg)`;
  LamhaMotion.exit(ghost, [
    { opacity: 1, transform: "none" },
    { opacity: 0.9, transform: grade === "hard" ? "translateY(10px) scale(.98)" : `translateX(${dx * 0.25}px) rotate(${dx > 0 ? 2 : -2}deg)`, offset: 0.3 },
    { opacity: 0, transform: to }
  ], { duration: 280, easing: "cubic-bezier(.5,0,.75,0)", fullOnly: true }).then(() => ghost.remove());
}

/** The example sentence with the looked-up form of the word marked. */
function highlight(sentence, word) {
  const i = sentence.toLowerCase().indexOf(String(word).toLowerCase());
  if (i < 0 || !word) return sentence;
  return [sentence.slice(0, i), h("mark", null, sentence.slice(i, i + word.length)), sentence.slice(i + word.length)];
}

function gradeBtn(grade, label, ms, key) {
  return h("button", { class: grade, title: t("rv.key", { k: key }), onclick: () => grade && answer(grade) }, label, h("small", null, spanLabel(ms)));
}

function reveal() {
  if (!rv.queue[0] || rvShown) return;
  rvShown = true;
  renderReview();
}

async function answer(grade) {
  const c = rv.queue[0];
  if (!c || !rvShown) return;
  rvShown = false;
  flyOff(grade);
  rvEnter = true;
  rvSession.n++;
  rvSession[grade]++;
  const res = await browser.runtime.sendMessage({ type: "reviewGrade", key: c.key, grade }).catch(() => null);
  await loadReview(true);
  if (res && res.goal) { flash(t("td.goalDone", { n: res.goal })); LamhaMotion.burst(document.querySelector(".flash")); }
}

async function removeCurrent(c) {
  await browser.runtime.sendMessage({ type: "cardRemove", key: c.key }).catch(() => {});
  flash(t("rv.removed"));
  await loadReview(true);
}

document.addEventListener("keydown", e => {
  if (mode !== "review" || e.ctrlKey || e.metaKey || e.altKey || /^(TEXTAREA|INPUT)$/.test(e.target.tagName)) return;
  if (e.key === " " || e.key === "Enter") { if (!rvShown) { e.preventDefault(); reveal(); } return; }
  const grade = { 1: "again", 2: "hard", 3: "good", "١": "again", "٢": "hard", "٣": "good" }[e.key];
  if (grade && rvShown) { e.preventDefault(); answer(grade); }
});

function speakIcon() {
  const NS = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(NS, "svg");
  Object.entries({ viewBox: "0 0 24 24", width: 18, height: 18, fill: "none", stroke: "currentColor", "stroke-width": 2, "stroke-linecap": "round", "stroke-linejoin": "round" }).forEach(([k, v]) => svg.setAttribute(k, v));
  ["M11 5 6 9H2v6h4l5 4V5z", "M15.54 8.46a5 5 0 0 1 0 7.07"].forEach(d => {
    const p = document.createElementNS(NS, "path"); p.setAttribute("d", d); svg.append(p);
  });
  return svg;
}

/* ---- history ---- */

let histCount = 0;
/** Recent lookups and the Today card belong to the Translate tab: elsewhere they only make the popup taller.
 *  Today steps aside while a result is showing. */
function refreshHistVisibility() {
  $("histCard").hidden = !histCount || mode !== "translate";
  $("todayCard").hidden = mode !== "translate" || !!q.value.trim() || !$("todayCard").childElementCount;
}

/* ---- today: the daily goal (words looked up + review answers), the streak and the word of the day ---- */

let todayToken = 0;
async function renderToday() {
  const token = ++todayToken;
  const info = await browser.runtime.sendMessage({ type: "today" }).catch(() => null);
  if (token !== todayToken) return;
  const card = $("todayCard");
  if (!info) { card.replaceChildren(); refreshHistVisibility(); return; }
  const { done, goal, streak, word } = info;
  const met = goal > 0 && done >= goal;
  const head = (goal > 0 || streak >= 2) && h("div", { class: "td-goal" + (met ? " met" : "") },
    goal > 0 && h("div", { class: "td-ring", role: "img", "aria-label": t("td.ring", { done, goal }) }, ring(done / goal), h("span", { class: "td-n" }, num(done))),
    goal > 0 && h("div", { class: "td-text" }, h("b", null, t("td.progress", { done, goal })), h("span", null, met ? t("td.met") : t("td.left", { n: goal - done }))),
    streak >= 2 && h("span", { class: "td-streak", title: t("td.streakTitle") }, t("td.streak", { n: streak })));
  card.replaceChildren(...[head, word && wordOfDay(word)].filter(Boolean));
  refreshHistVisibility();
  if (met && Number(card.dataset.done) < goal && !card.hidden) LamhaMotion.burst(card.querySelector(".td-ring")); // reached it just now
  card.dataset.done = String(done);
}

/** The goal's ring: a circle filled to `p` (0…1), in the button colour, green when done. */
function ring(p) {
  const NS = "http://www.w3.org/2000/svg", C = 2 * Math.PI * 16;
  const svg = document.createElementNS(NS, "svg");
  Object.entries({ viewBox: "0 0 40 40", width: 44, height: 44, "aria-hidden": "true" }).forEach(([k, v]) => svg.setAttribute(k, v));
  for (const [cls, dash] of [["td-track", 0], ["td-fill", C * (1 - Math.min(1, Math.max(0, p)))]]) {
    const c = document.createElementNS(NS, "circle");
    Object.entries({ class: cls, cx: 20, cy: 20, r: 16, fill: "none", "stroke-width": 4, "stroke-linecap": "round", "stroke-dasharray": C, "stroke-dashoffset": dash, transform: "rotate(-90 20 20)" }).forEach(([k, v]) => c.setAttribute(k, v));
    svg.append(c);
  }
  return svg;
}

/** The word of the day: the word, its meaning and a sentence; "see its meaning" looks it up here. */
function wordOfDay(w) {
  const speakBtn = h("button", { class: "icon-btn", title: t("common.listen"), "aria-label": t("common.listen"), onclick: () => speakWord(speakBtn, w.q) }, speakIcon());
  return h("div", { class: "td-word" },
    h("div", { class: "td-label", title: w.from === "deck" ? t("td.wordDeckTitle") : null }, t(w.from === "deck" ? "td.wordDeck" : "td.wordNew")),
    h("div", { class: "td-w-row" },
      h("span", { class: "td-w", dir: "ltr" }, w.q), speakBtn,
      h("span", { class: "grow" }),
      w.tr && h("span", { class: "td-tr", dir: "auto" }, w.tr)),
    (w.def || w.ex) && h("div", { class: "td-def", dir: "ltr" }, w.def, w.ex && h("span", { class: "td-ex" }, `“${w.ex}”`)),
    h("button", { class: "link td-more", type: "button", onclick: () => { q.value = w.q; q.dispatchEvent(new Event("input")); clearTimeout(qTimer); runQuick(); } }, t("td.lookUp")));
}

/** Says an English word; the button shows sound waves until it's done. */
async function speakWord(btn, text) {
  btn.classList.add("playing");
  await browser.runtime.sendMessage({ type: "speak", text, lang: "en" }).catch(() => {});
  btn.classList.remove("playing");
}

async function renderHistory() {
  const { history = [] } = await browser.storage.local.get("history");
  histCount = history.length;
  refreshHistVisibility();
  $("hist").replaceChildren(...history.slice(0, 12).map(item => // 6 in Firefox's popup, 12 in the desktop window (CSS)
    h("li", null, h("button", {
      class: "hist-btn", type: "button", title: t("p.translateAgain"),
      onclick: () => { q.value = item.q; q.dispatchEvent(new Event("input")); clearTimeout(qTimer); runQuick(); }
    },
      h("span", { class: "w", dir: "auto" }, item.q),
      h("span", { class: "t" }, item.tr)))
  ));
}
$("clearHist").addEventListener("click", async () => {
  const { history = [] } = await browser.storage.local.get("history");
  await browser.storage.local.set({ history: [] });
  renderHistory();
  // no confirm() in the toolbar popup: an undo instead
  flash(t("p.histCleared"), { label: t("common.undo"), run: async () => { await browser.storage.local.set({ history }); renderHistory(); } });
});

init();
