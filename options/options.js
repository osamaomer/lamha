"use strict";

const DEFAULTS = {
  enabled: true, targetLang: "ar", triggerMode: "button", reverseForArabic: true, dictSource: "local", useContext: true,
  showInInputs: false, showWikipedia: true, translateDefinitions: true, autoSpeak: false,
  theme: "auto", motion: "auto", saveHistory: true, aiModel: "claude-opus-5", aiInInputs: true, saveMistakes: true, cardsAuto: true, cardsNewPerDay: 10, disabledSites: []
};
const BOOLS = ["useContext", "reverseForArabic", "translateDefinitions", "showWikipedia", "autoSpeak", "showInInputs", "saveHistory", "aiInInputs", "saveMistakes", "cardsAuto"];
const { t, num } = LamhaI18n;
const OWN_ERRORS = ["ai_bad_key", "ai_no_credit", "ai_forbidden", "ai_model", "ai_rate_limited", "ai_busy", "ai_timeout", "network", "ollama_offline", "ollama_origin", "ollama_model"];
const aiErrorText = code => (OWN_ERRORS.includes(code) ? t("oerr." + code) : null)
  || (LamhaAI.ERRORS[code] && LamhaAI.errorInfo(code).slice(0, 2).join(" — "))
  || t("o.connectFailed") + String(code || "").replace(/^ai_error:/, "");
const $ = id => document.getElementById(id);

let savedTimer;
function saved() {
  const el = $("saved");
  el.classList.add("show");
  clearTimeout(savedTimer);
  savedTimer = setTimeout(() => el.classList.remove("show"), 1200);
}
const save = patch => browser.storage.sync.set(patch).then(saved);

/** "Alt+Shift+L" → <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd>, written as the popup shows it. */
function kbd(combo) {
  return (combo || "—").split("+").flatMap((k, i) => {
    const el = document.createElement("kbd");
    el.textContent = k;
    return i ? ["+", el] : [el];
  });
}

async function init() {
  await LamhaI18n.init({ onChange: () => location.reload() }); // the whole page is redrawn in the new language
  LamhaI18n.applyDom(document);
  document.querySelectorAll("#cardsNewPerDay option").forEach(o => { o.textContent = num(Number(o.value)); });
  const { uiLang = "auto" } = await browser.storage.sync.get("uiLang");
  $("uiLang").value = uiLang;
  $("uiLang").addEventListener("change", e => save({ uiLang: e.target.value }));
  const s = { ...DEFAULTS, ...(await browser.storage.sync.get(DEFAULTS)) };

  document.querySelectorAll('input[name="triggerMode"]').forEach(r => {
    r.checked = r.value === s.triggerMode;
    r.addEventListener("change", () => r.checked && save({ triggerMode: r.value }));
  });
  document.querySelectorAll('input[name="dictSource"]').forEach(r => {
    r.checked = r.value === s.dictSource;
    r.addEventListener("change", () => r.checked && save({ dictSource: r.value }));
  });
  browser.runtime.sendMessage({ type: "dictMeta" }).then(m => {
    if (!m || !m.entries) return;
    $("dictInfo").textContent = t("o.dictCounts", m);
  }).catch(() => {});

  BOOLS.forEach(k => {
    $(k).checked = !!s[k];
    $(k).addEventListener("change", e => save({ [k]: e.target.checked }));
  });
  await LamhaMotion.ready;
  LamhaMotion.attach(document.documentElement);
  initMotionSetting(s.motion);
  ["targetLang", "theme", "aiModel"].forEach(k => {
    $(k).value = s[k];
    $(k).addEventListener("change", e => save({ [k]: e.target.value }));
  });

  renderSites(s.disabledSites);
  renderHistCount();
  initAI();
  renderJournal();
  renderReviewStats();
  $("cardsNewPerDay").value = String(s.cardsNewPerDay);
  $("cardsNewPerDay").addEventListener("change", e => save({ cardsNewPerDay: Number(e.target.value) }));
  $("rvClear").addEventListener("click", async () => {
    if (!confirm(t("o.confirmDeleteCards"))) return;
    await browser.storage.local.set({ cards: {}, cardStats: {}, cardsImported: true });
    saved();
  });
  $("jClear").addEventListener("click", async () => {
    if (!confirm(t("o.confirmClearJournal"))) return;
    await browser.storage.local.remove("mistakes");
    saved();
  });
  buildToc();
  // the desktop app adds its own sections (clipboard, updates) after the page loads
  new MutationObserver(buildToc).observe(document.querySelector(".wrap"), { childList: true });
  if (location.hash) { const target = document.getElementById(location.hash.slice(1)); if (target) target.scrollIntoView({ block: "start" }); }

  // shortcuts
  const cmds = await browser.commands.getAll();
  const get = n => (cmds.find(c => c.name === n) || {}).shortcut;
  $("k-lookup").replaceChildren(...kbd(get("lookup-selection")));
  $("k-page").replaceChildren(...kbd(get("translate-page")));
  $("k-write").replaceChildren(...kbd(get("writing-tools")));
  if (browser.commands.openShortcutSettings) {
    $("shortcuts").hidden = false;
    $("shortcuts").addEventListener("click", () => browser.commands.openShortcutSettings());
  }

  $("ver").textContent = browser.runtime.getManifest().version;

  // welcome + permission
  const params = new URLSearchParams(location.search);
  const hasPerm = await browser.permissions.contains({ origins: ["<all_urls>"] });
  if (params.has("welcome") || !hasPerm) $("welcome").hidden = false;
  $("permBox").hidden = hasPerm;
  $("grant").addEventListener("click", async () => {
    if (await browser.permissions.request({ origins: ["<all_urls>"] })) $("permBox").hidden = true;
  });

  $("clearHist").addEventListener("click", async () => {
    if (!confirm(t("o.confirmClearHistory"))) return;
    await browser.storage.local.set({ history: [] });
    renderHistCount(); saved();
  });
}

/** Links to the page's sections, named by their headings (so they follow the interface language). */
let tocObserver = null;
function buildToc() {
  if (tocObserver) tocObserver.disconnect();
  const sections = [...document.querySelectorAll("section.panel[id]:not(#welcome)")].filter(s => !s.hidden && s.querySelector("h2"));
  const links = sections.map(s => {
    const a = document.createElement("a");
    a.href = "#" + s.id;
    a.textContent = s.querySelector("h2").textContent.replace(/[^\p{L}\p{N}\s()/-]/gu, "").trim(); // without the emoji
    return a;
  });
  const ink = document.createElement("span"); // the highlight that slides to the section in view (shared/motion.css)
  ink.className = "toc-ink";
  ink.hidden = true;
  $("toc").replaceChildren(ink, ...links);
  $("toc").classList.add("has-ink");
  const moveInk = a => {
    const first = ink.hidden;
    if (first) ink.style.transition = "none"; // appears in place, then slides from there
    ink.hidden = false;
    Object.assign(ink.style, { width: a.offsetWidth + "px", height: a.offsetHeight + "px", transform: `translate(${a.offsetLeft}px, ${a.offsetTop}px)` });
    if (first) requestAnimationFrame(() => { ink.style.transition = ""; });
  };
  const mark = id => links.forEach(a => {
    const on = a.hash === "#" + id;
    a.setAttribute("aria-current", String(on));
    if (on) { a.scrollIntoView({ block: "nearest", inline: "nearest" }); moveInk(a); }
  });
  if (typeof IntersectionObserver !== "function") return;
  const io = tocObserver = new IntersectionObserver(entries => {
    const top = entries.filter(e => e.isIntersecting).sort((x, y) => x.boundingClientRect.top - y.boundingClientRect.top)[0];
    if (top) mark(top.target.id);
  }, { rootMargin: "-64px 0px -60% 0px" });
  sections.forEach(s => io.observe(s));
}

/** Settings → Appearance → Animations, with a small card that shows what the chosen level looks like. */
function initMotionSetting(value) {
  const sel = $("motion");
  sel.value = LamhaMotion.SETTINGS.includes(value) ? value : "auto";
  const note = () => {
    const lv = LamhaMotion.level();
    $("mdNote").textContent = (sel.value === "auto" ? t("o.motionAutoIs", { level: t("motion." + lv) }) + " — " : "") + t("o.motionNote_" + lv);
  };
  const demo = () => {
    const card = $("mdCard"), word = card.querySelector(".md-t");
    if (LamhaMotion.full()) {
      LamhaMotion.play(card, [{ opacity: 0, transform: "scale(.86) translateY(-6px)" }, { opacity: 1, transform: "none" }], { duration: 280, easing: "cubic-bezier(.2,.9,.3,1.25)" });
    } else {
      LamhaMotion.play(card, [{ opacity: 0, transform: "translateY(-3px)" }, { opacity: 1, transform: "none" }], { duration: 180 });
    }
    LamhaMotion.play(word, [{ opacity: 0, transform: "translateY(6px) scale(.95)" }, { opacity: 1, transform: "none" }], { duration: 260, delay: 120, fullOnly: true });
  };
  sel.addEventListener("change", () => {
    LamhaMotion.use(sel.value); // at once on this page; the others follow the saved setting
    save({ motion: sel.value });
    note();
    demo();
  });
  $("mdPlay").addEventListener("click", demo);
  LamhaMotion.onChange(note); // e.g. Windows' animation effects switched meanwhile
  LamhaMotion.ready.then(note);
}

/* Writing tools. Provider, Claude API key and Ollama model are kept in storage.local
 * (per device, never synced) and checked with a tiny request before they are relied on. */
const setStatus = (id, text, cls = "") => { $(id).textContent = text; $(id).className = "ai-status " + cls; };
const bg = msg => browser.runtime.sendMessage(msg).catch(e => ({ ok: false, error: String(e) }));

async function initAI() {
  const local = await browser.storage.local.get(["aiKey", "aiProvider", "ollamaUrl", "ollamaModel", "geminiKey", "geminiModel"]);
  const provider = LamhaAI.provider(local).id;
  const showProvider = p => {
    $("ollamaBox").hidden = p !== "ollama";
    $("geminiBox").hidden = p !== "gemini";
    $("claudeBox").hidden = p !== "claude";
    if (p === "ollama") refreshOllama();
  };
  document.querySelectorAll('input[name="aiProvider"]').forEach(r => {
    r.checked = r.value === provider;
    r.addEventListener("change", async () => {
      if (!r.checked) return;
      await browser.storage.local.set({ aiProvider: r.value });
      showProvider(r.value);
      saved();
    });
  });
  $("ollamaUrl").value = local.ollamaUrl || "";
  initKeyBox({ provider: "claude", storageKey: "aiKey", input: "aiKey", save: "aiSave", remove: "aiRemove", status: "aiStatus", model: "aiModel", placeholder: "sk-ant-api…" }, !!local.aiKey);
  initKeyBox({ provider: "gemini", storageKey: "geminiKey", input: "geminiKey", save: "geminiSave", remove: "geminiRemove", status: "geminiStatus", model: "geminiModel", placeholder: "AIza…" }, !!local.geminiKey);
  $("geminiModel").value = local.geminiModel || "gemini-3.8-flash";
  $("geminiModel").addEventListener("change", async e => { await browser.storage.local.set({ geminiModel: e.target.value }); saved(); });
  initOllama();
  showProvider(provider);
}

/* ---- review deck ---- */

async function renderReviewStats() {
  const res = await browser.runtime.sendMessage({ type: "reviewQueue" }).catch(() => null);
  if (!res || !res.counts.total) return;
  const { due, fresh, total, learned } = res.counts;
  $("rvSummary").textContent = t("o.rvSummary", { total, learned, due, fresh });
}

/* ---- mistake journal ---- */

let journalCat = ""; // category whose tip and mistakes are shown ("" = all recent)
let journalShown = false;
const arNum = n => num(n);

function el(tag, cls, ...kids) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  e.append(...kids.filter(k => k != null && k !== false));
  return e;
}

async function renderJournal() {
  const { mistakes } = await browser.storage.local.get("mistakes");
  const j = mistakes || { checks: 0, counts: {}, recent: [] };
  const total = Object.values(j.counts).reduce((a, b) => a + b, 0);
  const rows = Object.entries(j.counts).filter(([c, n]) => LamhaAI.isCategory(c) && n > 0) // names and rules: LamhaAI.catLabel / catTip.sort((a, b) => b[1] - a[1]);

  if (!j.checks) {
    $("jSummary").textContent = t("o.jEmpty");
  } else {
    const top = rows[0] && rows[0][0] !== "other" ? t("o.jTop", { cat: LamhaAI.catLabel(rows[0][0]) }) : "";
    $("jSummary").textContent = t("o.jSummary", { checks: j.checks, total, top });
  }

  if (journalCat && !j.counts[journalCat]) journalCat = "";
  const max = rows.length ? rows[0][1] : 1;
  $("jBars").classList.toggle("lm-first", !journalShown && rows.length > 0); // bars grow and count up the first time only
  const counting = !journalShown;
  if (rows.length) journalShown = true;
  $("jBars").replaceChildren(...rows.map(([c, n]) => {
    const fill = el("span", "fill");
    fill.style.width = Math.max(4, Math.round((n / max) * 100)) + "%";
    const count = el("span", "n", arNum(n));
    if (counting) LamhaMotion.countUp(count, n, { format: arNum, duration: 700 });
    const b = el("button", "bar" + (c === journalCat ? " on" : ""), el("span", null, ...labelParts(LamhaAI.catLabel(c))), el("span", "meter", fill), count);
    b.setAttribute("aria-pressed", String(c === journalCat));
    b.addEventListener("click", () => { journalCat = journalCat === c ? "" : c; renderJournal(); });
    return b;
  }));

  const tip = journalCat && LamhaAI.catTip(journalCat);
  $("jTip").hidden = !tip;
  if (tip) $("jTip").replaceChildren(el("b", null, t("o.rule")), tip);

  const list = j.recent.filter(r => !journalCat || r.cat === journalCat).slice(0, 30);
  $("jRecentBox").hidden = !list.length;
  $("jRecentTitle").textContent = journalCat ? t("o.examplesOf", { cat: LamhaAI.catLabel(journalCat) }) : t("o.recentMistakes");
  $("jRecent").replaceChildren(...list.map(r => el("li", null,
    el("div", "fix", el("del", null, r.original), " → ", el("ins", null, r.fix)),
    r.why && el("div", "why", r.why)
  )));
}

/** "أدوات التعريف والتنكير (a / an / the)" → the Arabic name, then the English part isolated and unbroken. */
function labelParts(label) {
  const m = /^(.*?)\s*(\([^)]*[A-Za-z][^)]*\))$/.exec(label);
  if (!m) return [label];
  const bdi = document.createElement("bdi");
  bdi.textContent = m[2];
  return [m[1] + " ", bdi];
}

function initOllama() {
  $("ollamaUrl").addEventListener("change", async () => {
    await browser.storage.local.set({ ollamaUrl: $("ollamaUrl").value.trim() });
    saved();
    refreshOllama();
  });
  $("ollamaRefresh").addEventListener("click", refreshOllama);
  $("ollamaModel").addEventListener("change", async () => {
    await browser.storage.local.set({ ollamaModel: $("ollamaModel").value });
    saved();
    testOllama();
  });
  $("ollamaTest").addEventListener("click", testOllama);
}

async function refreshOllama() {
  setStatus("ollamaStatus", t("o.ollamaConnecting"));
  const res = await bg({ type: "ollamaModels", url: $("ollamaUrl").value.trim() });
  const sel = $("ollamaModel");
  if (!res || !res.ok) { setStatus("ollamaStatus", aiErrorText(res && res.error), "bad"); return; }
  const { ollamaModel = "" } = await browser.storage.local.get("ollamaModel");
  const models = res.data;
  sel.replaceChildren(...[{ name: "", size: 0 }, ...models].map(m => {
    const o = document.createElement("option");
    o.value = m.name;
    o.textContent = m.name ? `${m.name} (${(m.size / 1e9).toFixed(1)} GB)` : t("o.pickModel");
    return o;
  }));
  if (!models.length) {
    setStatus("ollamaStatus", t("o.ollamaNoModels"), "bad");
    return;
  }
  let pick = models.some(m => m.name === ollamaModel) ? ollamaModel : "";
  if (!pick) { // first time: prefer a Qwen model (strong Arabic), else the first one
    pick = (models.find(m => /^qwen/i.test(m.name)) || models[0]).name;
    await browser.storage.local.set({ ollamaModel: pick });
  }
  sel.value = pick;
  setStatus("ollamaStatus", t("o.ollamaOk", { n: models.length }) + (plainHttpRemote($("ollamaUrl").value) ? t("o.ollamaPlainHttp") : ""), "ok");
}

/** http:// to a machine other than this one: the text travels unencrypted. */
function plainHttpRemote(url) {
  try {
    const u = new URL(String(url).trim());
    return u.protocol === "http:" && !/^(localhost|127(\.\d+){3}|\[::1\])$/i.test(u.hostname);
  } catch (_) { return false; }
}

async function testOllama() {
  const model = $("ollamaModel").value;
  if (!model) { setStatus("ollamaStatus", t("o.pickModelFirst"), "bad"); return; }
  $("ollamaTest").disabled = true;
  setStatus("ollamaStatus", t("o.ollamaTesting", { model }));
  const t0 = performance.now();
  const res = await bg({ type: "aiTest", provider: "ollama", model, url: $("ollamaUrl").value.trim() });
  $("ollamaTest").disabled = false;
  if (!res || !res.ok) { setStatus("ollamaStatus", aiErrorText(res && res.error), "bad"); return; }
  const secs = num((performance.now() - t0) / 1000, { maximumFractionDigits: 1 });
  setStatus("ollamaStatus", t("o.ollamaWorks", { secs }), "ok");
}

/** An API-key box (Claude or Gemini): checked with a tiny request, then kept in storage.local. */
function initKeyBox({ provider, storageKey, input, save, remove, status: statusId, model, placeholder }, hasKey) {
  const status = (text, cls) => setStatus(statusId, text, cls);
  const showSaved = has => {
    $(input).value = "";
    $(input).placeholder = has ? t("o.keySavedPlaceholder") : placeholder;
    $(remove).hidden = !has;
    status(has ? t("o.keySaved") : t("o.noKey"), has ? "ok" : "");
  };
  showSaved(hasKey);

  $(save).addEventListener("click", async () => {
    const key = $(input).value.trim();
    if (!key) { $(input).focus(); return; }
    $(save).disabled = true;
    status(t("o.checkingKey"));
    const res = await bg({ type: "aiTest", provider, key, model: $(model).value });
    $(save).disabled = false;
    if (!res || !res.ok) {
      // the service's own words help when something unexpected happens
      status(aiErrorText(res && res.error) + (res && res.detail ? `  (${res.detail})` : ""), "bad");
      return;
    }
    await browser.storage.local.set({ [storageKey]: key });
    showSaved(true);
    if (res.note === "gemini_busy") status(t("o.geminiBusySaved"), "ok");
    saved();
  });
  $(input).addEventListener("keydown", e => { if (e.key === "Enter") $(save).click(); });
  $(remove).addEventListener("click", async () => {
    await browser.storage.local.remove(storageKey);
    showSaved(false);
    saved();
  });
}

function renderSites(list) {
  const ul = $("sites");
  if (!list.length) {
    const li = document.createElement("li");
    li.className = "empty";
    li.textContent = t("o.noSites");
    ul.replaceChildren(li);
    return;
  }
  ul.replaceChildren(...list.map(site => {
    const li = document.createElement("li");
    const name = document.createElement("span");
    name.textContent = site;
    const btn = document.createElement("button");
    btn.className = "link";
    btn.textContent = t("common.remove");
    btn.addEventListener("click", async () => {
      const { disabledSites = [] } = await browser.storage.sync.get("disabledSites");
      const next = disabledSites.filter(x => x !== site);
      await save({ disabledSites: next });
      renderSites(next);
    });
    li.append(name, btn);
    return li;
  }));
}

async function renderHistCount() {
  const { history = [] } = await browser.storage.local.get("history");
  $("histCount").textContent = history.length ? t("o.histCount", { n: history.length }) : t("o.histEmpty");
}

browser.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.disabledSites) renderSites(changes.disabledSites.newValue || []);
  if (area === "local" && changes.mistakes) renderJournal();
  if (area === "local" && changes.cards) renderReviewStats();
  if (area === "sync" && changes.cardsNewPerDay) renderReviewStats();
});

init();
