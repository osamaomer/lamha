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
  await LamhaI18n.init({ onChange: () => location.reload() }); // redrawn in the new language
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
  await initCompose();
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
  clearTimeout(qTimer);
  qTimer = setTimeout(runQuick, 420);
});
q.addEventListener("keydown", e => {
  if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); clearTimeout(qTimer); runQuick(); }
});
$("clearQ").addEventListener("click", () => {
  q.value = ""; q.dispatchEvent(new Event("input")); $("result").hidden = true; q.focus();
});

const dirOf = l => (["ar", "fa", "ur", "he", "iw"].includes(l) ? "rtl" : "ltr");

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
    const msg = res && res.error === "rate_limited" ? t("p.busy") : t("p.failed");
    out.replaceChildren(h("div", { class: "error" }, msg));
    return;
  }
  const d = res.data;
  const kids = [
    h("div", { class: "main" },
      h("div", { style: "flex:1;min-width:0" },
        h("div", { class: "tr" + (d.translation.length > 40 ? " long" : ""), dir: dirOf(d.tl) }, d.translation),
        d.type === "word" && d.srcTranslit && h("div", { class: "phon" }, `${d.query} · /${d.srcTranslit}/`)
      ),
      h("button", { class: "icon-btn", title: t("common.copy"), "aria-label": t("common.copy"), onclick: () => navigator.clipboard.writeText(d.translation) }, copyIcon())
    )
  ];
  (d.dict || []).slice(0, 3).forEach(p => kids.push(
    h("div", { class: "pos-row" }, h("span", { class: "pos" }, p.pos), h("span", { class: "terms" }, p.terms.slice(0, 6).map(term => term.word).join(LamhaI18n.lang() === "ar" ? "، " : ", ")))
  ));
  const firstDef = d.definitions && d.definitions[0] && d.definitions[0].entries[0];
  if (firstDef) kids.push(h("div", { class: "def" }, firstDef.glossTr && h("div", null, firstDef.glossTr), h("div", { class: "en" }, firstDef.gloss)));
  out.replaceChildren(...kids);
  renderHistory();
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
  ["friendly", "tool.friendly"], ["concise", "tool.concise"], ["toEnglish", "tool.toEnglishShort"]
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
  browser.storage.local.set({ popupMode: m });
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
      class: id === active ? "on" : null, disabled: !draft.value.trim() || null, onclick: () => runWrite(id)
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

function wrError(code, retry) {
  const [title, text, needsSettings] = LamhaAI.errorInfo(code, providerName());
  return h("div", { class: "error" }, h("b", null, title), text,
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
  const res = await browser.runtime.sendMessage({ type: "ai", tool, text, extra: fresh ? { fresh: true } : {} }).catch(e => ({ ok: false, error: String(e) }));
  if (token !== wrToken) return;
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
    out.replaceChildren(h("div", { class: "text", dir: "auto" }, result), actions(result));
    return;
  }
  const { corrected = "", issues = [] } = res.data;
  if (!issues.length || corrected.trim() === text) {
    out.replaceChildren(h("div", { class: "ok" }, t("write.noErrorsCheck")));
    return;
  }
  const fixed = corrected.trim();
  out.replaceChildren(
    h("div", { class: "text", dir: "ltr" }, LamhaAI.diffNodes(h, text, fixed)),
    actions(fixed),
    h("ul", { class: "issues" }, issues.map(i => h("li", null,
      h("div", { class: "fix" }, h("del", null, i.original), " → ", h("ins", null, i.fix)),
      i.category !== "other" && h("span", { class: "cat" }, LamhaAI.catLabel(i.category)),
      i.why && h("div", { class: "why" }, i.why)
    )))
  );
}

function flash(text) {
  const t = h("div", { class: "flash", role: "status" }, text);
  document.body.append(t);
  setTimeout(() => t.remove(), 1200);
}

/** One line under the compose box: the user's most frequent mistake type → the journal. */
async function renderWeak() {
  const { mistakes } = await browser.storage.local.get("mistakes");
  const top = Object.entries((mistakes && mistakes.counts) || {})
    .filter(([c]) => c !== "other" && LamhaAI.CATEGORIES[c])
    .sort((a, b) => b[1] - a[1])[0];
  const btn = $("weak");
  btn.hidden = !top;
  if (!top) return;
  btn.replaceChildren(t("write.topMistake"), h("b", null, LamhaAI.catLabel(top[0])), t("write.learnRule", { n: top[1] }));
  btn.onclick = () => { browser.tabs.create({ url: browser.runtime.getURL("options/options.html#journal") }); window.close(); };
}

browser.storage.onChanged.addListener((changes, area) => {
  if (area !== "local") return;
  LamhaAI.PROVIDER_KEYS.forEach(k => { if (changes[k]) aiLocal[k] = changes[k].newValue; });
  if (changes.mistakes) renderWeak();
});

/* ---- review: flashcards of looked-up words (scheduling lives in the background) ---- */

let rv = { queue: [], counts: null, nextDue: 0 }, rvShown = false;
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
    h("span", { class: "pill" }, t("rv.due", { n: counts.due })),
    h("span", { class: "pill new" }, t("rv.fresh", { n: counts.fresh })),
    h("span", { class: "grow" }),
    h("span", null, t("rv.totals", counts))
  );
  if (!c) {
    $("rvCard").replaceChildren(counts.total
      ? h("div", { class: "rv-done" }, h("b", null, t("rv.doneTitle")),
        rv.nextDue ? t("rv.next", { span: spanLabel(rv.nextDue - Date.now()) }) : "")
      : h("div", { class: "rv-done" }, h("b", null, t("rv.emptyTitle")), t("rv.emptyText")));
    return;
  }

  const front = h("div", { class: "rv-front" },
    c.isNew && h("div", { class: "rv-tag" }, t("rv.newWord")),
    h("div", { class: "rv-word-row" },
      h("div", { class: "rv-word" }, c.q),
      h("button", { class: "icon-btn", title: t("common.listen"), "aria-label": t("common.listen"), onclick: () => browser.runtime.sendMessage({ type: "speak", text: c.q, lang: "en" }).catch(() => {}) }, speakIcon())),
    c.ex && h("div", { class: "rv-ex" }, highlight(c.ex, c.form || c.q))
  );
  const kids = [front];
  if (!rvShown) {
    kids.push(h("button", { class: "btn block rv-show", onclick: reveal }, t("rv.show"), h("kbd", null, "Space")));
  } else {
    kids.push(
      h("div", { class: "rv-back" },
        h("div", { class: "rv-tr" }, c.tr),
        c.def && h("div", { class: "rv-def" }, c.def)),
      h("div", { class: "rv-grades" },
        gradeBtn("again", t("rv.again"), c.next.again, "1"),
        gradeBtn("hard", t("rv.hard"), c.next.hard, "2"),
        gradeBtn("good", t("rv.good"), c.next.good, "3")),
      h("div", { class: "rv-tools" },
        h("button", { class: "link", onclick: () => removeCurrent(c) }, t("rv.remove")))
    );
  }
  $("rvCard").replaceChildren(...kids);
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
  await browser.runtime.sendMessage({ type: "reviewGrade", key: c.key, grade }).catch(() => {});
  await loadReview(true);
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

async function renderHistory() {
  const { history = [] } = await browser.storage.local.get("history");
  $("histCard").hidden = !history.length;
  $("hist").replaceChildren(...history.slice(0, 6).map(item =>
    h("li", { title: t("p.translateAgain"), onclick: () => { q.value = item.q; q.dispatchEvent(new Event("input")); clearTimeout(qTimer); runQuick(); } },
      h("span", { class: "w", dir: "auto" }, item.q),
      h("span", { class: "t" }, item.tr))
  ));
}
$("clearHist").addEventListener("click", async () => {
  await browser.storage.local.set({ history: [] });
  renderHistory();
});

init();
