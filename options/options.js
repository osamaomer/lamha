"use strict";

const DEFAULTS = {
  enabled: true, targetLang: "ar", triggerMode: "button", reverseForArabic: true, dictSource: "local", useContext: true,
  showInInputs: false, showWikipedia: true, translateDefinitions: true, autoSpeak: false,
  theme: "auto", motion: "auto", saveHistory: true, enDict: false, explainLangs: [], aiModel: "claude-opus-5", aiInInputs: true, writeOnDblClick: true, saveMistakes: true, cardsAuto: true, cardsNewPerDay: 10, dailyGoal: 10, disabledSites: []
};
const BOOLS = ["enDict", "useContext", "reverseForArabic", "translateDefinitions", "showWikipedia", "autoSpeak", "showInInputs", "saveHistory", "aiInInputs", "writeOnDblClick", "saveMistakes", "cardsAuto"];
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
  document.querySelectorAll("#cardsNewPerDay option, #dailyGoal option:not([value='0'])").forEach(o => { o.textContent = num(Number(o.value)); });
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

  renderExplainLangs(s.explainLangs);
  renderPacks();
  renderSites(s.disabledSites);
  renderHistCount();
  initAI();
  initTranslation();
  renderJournal();
  renderReviewStats();
  $("cardsNewPerDay").value = String(s.cardsNewPerDay);
  $("cardsNewPerDay").addEventListener("change", e => save({ cardsNewPerDay: Number(e.target.value) }));
  $("dailyGoal").value = String([0, 5, 10, 20, 30].includes(s.dailyGoal) ? s.dailyGoal : 10);
  $("dailyGoal").addEventListener("change", e => save({ dailyGoal: Number(e.target.value) }));
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
    a.textContent = s.querySelector("h2").textContent.replace(/\p{Extended_Pictographic}\uFE0F?/gu, "").trim(); // without any emoji (keeps "&")
    return a;
  });
  const ink = document.createElement("span"); // the highlight that slides to the section in view (shared/motion.css)
  ink.className = "toc-ink";
  ink.hidden = true;
  const row = document.createElement("div"); // the scrolling row inside the sticky bar: only it fades at the edges
  row.className = "toc-links";
  row.append(ink, ...links);
  row.addEventListener("scroll", tocEdges, { passive: true });
  $("toc").replaceChildren(row);
  $("toc").classList.add("has-ink");
  const moveInk = a => {
    const first = ink.hidden;
    if (first) ink.style.transition = "none"; // appears in place, then slides from there
    ink.hidden = false;
    Object.assign(ink.style, { width: a.offsetWidth + "px", height: a.offsetHeight + "px", transform: `translate(${a.offsetLeft}px, ${a.offsetTop}px)` });
    if (first) requestAnimationFrame(() => { ink.style.transition = ""; });
  };
  // The row only slides to show the marked link when the mouse isn't on it: moving links under the pointer made it
  // jump from one to the next while the user was aiming. A clicked link stays marked while the page settles.
  let pointerIn = false, held = "", heldUntil = 0;
  row.addEventListener("pointerenter", () => { pointerIn = true; });
  row.addEventListener("pointerleave", () => { pointerIn = false; });
  const bringIntoView = a => {
    const r = row.getBoundingClientRect(), b = a.getBoundingClientRect();
    if (pointerIn || (b.left >= r.left && b.right <= r.right)) return;
    row.scrollBy({ left: b.left < r.left ? b.left - r.left - 32 : b.right - r.right + 32, behavior: LamhaMotion.any() ? "smooth" : "auto" }); // the row only, never the page
  };
  const mark = id => {
    if (Date.now() < heldUntil && id !== held) return;
    links.forEach(a => {
      const on = a.hash === "#" + id;
      a.setAttribute("aria-current", String(on));
      if (on) { bringIntoView(a); moveInk(a); }
    });
  };
  links.forEach(a => a.addEventListener("click", () => { held = a.hash.slice(1); heldUntil = Date.now() + 1200; mark(held); }));
  setTimeout(tocEdges, 0); // once laid out
  if (typeof IntersectionObserver !== "function") return;
  const io = tocObserver = new IntersectionObserver(entries => {
    const top = entries.filter(e => e.isIntersecting).sort((x, y) => x.boundingClientRect.top - y.boundingClientRect.top)[0];
    if (top) mark(top.target.id);
  }, { rootMargin: "-64px 0px -60% 0px" });
  sections.forEach(s => io.observe(s));
}

/** The section links scroll sideways on a narrow window: fade the edge where more links are hidden, so it shows. */
function tocEdges() {
  const row = $("toc").querySelector(".toc-links");
  const links = row ? row.querySelectorAll("a") : [];
  if (!links.length) return;
  const box = row.getBoundingClientRect();
  const rects = [...links].map(a => a.getBoundingClientRect()).filter(r => r.width > 0); // links hidden in the desktop app don't count
  if (!rects.length) return;
  row.style.setProperty("--fade-l", Math.min(...rects.map(r => r.left)) < box.left - 1 ? "48px" : "0px");
  row.style.setProperty("--fade-r", Math.max(...rects.map(r => r.right)) > box.right + 1 ? "48px" : "0px");
}
addEventListener("resize", tocEdges, { passive: true });

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

/* ---- translation service (Settings → Translation): per device in storage.local, like the AI keys ---- */

const TR_MODELS = { // the first one is what background.js translates with by default
  gemini: [["gemini-3.5-flash-lite", "o.gemLite"], ["gemini-3.8-flash", "o.gemFlash"]],
  claude: [["claude-haiku-4-5", "o.haiku"], ["claude-sonnet-5", "o.sonnet"], ["claude-opus-5", "o.opus"]]
};
const TR_WATCH = ["trService", "trProvider", "trModels", "aiProvider", "ollamaModel", "ollamaUrl", "aiKey", "geminiKey", "aiKeySet", "geminiKeySet"];

async function initTranslation() {
  const st = await browser.storage.local.get([...TR_WATCH, "trPages"]);
  document.querySelectorAll('input[name="trService"]').forEach(r => {
    r.checked = r.value === LamhaAI.translator(st).service;
    r.addEventListener("change", async () => { if (r.checked) { await browser.storage.local.set({ trService: r.value }); saved(); } });
  });
  $("trProvider").value = ["ollama", "gemini", "claude"].includes(st.trProvider) ? st.trProvider : "writing";
  $("trProvider").addEventListener("change", async e => { await browser.storage.local.set({ trProvider: e.target.value }); saved(); });
  $("trPages").checked = st.trPages === true;
  $("trPages").addEventListener("change", async e => { await browser.storage.local.set({ trPages: e.target.checked }); saved(); });
  $("trModel").addEventListener("change", async e => {
    const { trModels = {} } = await browser.storage.local.get("trModels");
    await browser.storage.local.set({ trModels: { ...trModels, [LamhaAI.translator(st).id]: e.target.value } });
    saved();
  });

  let modelsFor = "";
  const render = () => {
    const tr = LamhaAI.translator(st);
    $("trAiBox").hidden = tr.service === "google";
    const key = [tr.id, st.ollamaModel, st.ollamaUrl].join("|");
    if (key !== modelsFor) { modelsFor = key; fillTrModels(tr.id, st); }
    setStatus("trStatus", tr.ready ? t("o.trUses", { p: tr.name }) : t("o.trNotReady", { p: tr.name }), tr.ready ? "ok" : "bad");
  };
  // a key saved or a provider picked under "Writing tools" changes what translation can use
  browser.storage.onChanged.addListener((changes, area) => {
    if (area !== "local" || !TR_WATCH.some(k => changes[k])) return;
    TR_WATCH.forEach(k => { if (changes[k]) st[k] = changes[k].newValue; });
    render();
  });
  render();
}

/** The model menu for the translating provider; for Ollama, the models installed there (after "same as writing"). */
async function fillTrModels(id, st) {
  const sel = $("trModel");
  const chosen = (st.trModels && typeof st.trModels === "object" ? st.trModels : {})[id];
  const opt = (value, label) => { const o = document.createElement("option"); o.value = value; o.textContent = label; return o; };
  $("trModelHint").textContent = t({ gemini: "o.trModelHintGemini", claude: "o.trModelHintClaude", ollama: "o.trModelHintOllama" }[id]);
  if (id !== "ollama") {
    sel.replaceChildren(...TR_MODELS[id].map(([v, k]) => opt(v, t(k))));
    sel.value = TR_MODELS[id].some(([v]) => v === chosen) ? chosen : TR_MODELS[id][0][0];
    return;
  }
  sel.replaceChildren(opt("", t("o.trOllamaSame", { m: st.ollamaModel || "—" })));
  const res = await bg({ type: "ollamaModels", url: st.ollamaUrl || "" });
  if (res && res.ok) sel.append(...res.data.map(m => opt(m.name, m.name)));
  if (chosen && ![...sel.options].some(o => o.value === chosen)) sel.append(opt(chosen, chosen)); // installed on another PC, or Ollama is off
  sel.value = chosen || "";
}

/* ---- review deck ---- */

async function renderReviewStats() {
  const res = await browser.runtime.sendMessage({ type: "reviewQueue" }).catch(() => null);
  if (!res) return;
  const { due, fresh, total, learned } = res.counts;
  // an empty deck (e.g. just cleared) shows the introduction again, not the old counts
  $("rvSummary").textContent = total ? t("o.rvSummary", { total, learned, due, fresh }) : t("o.reviewIntro");
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
  // most frequent first: the summary's "top" and the bar widths are both measured against rows[0]
  const rows = Object.entries(j.counts).filter(([c, n]) => LamhaAI.isCategory(c) && n > 0).sort((a, b) => b[1] - a[1]);

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

/** Settings → Dictionary: the languages whose words are explained in that language (English has its own switch above).
 *  The card's switch changes the same list, so it's redrawn when that happens with Settings open. */
const EXPLAIN_LANGS = ["ar", "fr", "tr", "ur", "fa", "es", "de"];
function renderExplainLangs(list) {
  const on = new Set([].concat(list || []));
  $("explainLangs").replaceChildren(...EXPLAIN_LANGS.map(code => {
    const b = document.createElement("button");
    b.type = "button";
    b.textContent = t("lang." + code);
    b.setAttribute("aria-pressed", String(on.has(code)));
    b.addEventListener("click", async () => {
      const { explainLangs = [] } = await browser.storage.sync.get({ explainLangs: [] });
      const rest = [].concat(explainLangs).filter(l => l !== code);
      await save({ explainLangs: on.has(code) ? rest : [...rest, code] }); // redrawn by the change listener below
    });
    return b;
  }));
}

/** Settings → Dictionary → Dictionaries to download (packs.js, through the background). While one downloads, the list
 *  is asked for again every 300 ms: it carries the download's progress. */
const packErrors = Object.create(null); // lang → the last download's error code
let packTimer = null;
async function renderPacks() {
  clearTimeout(packTimer);
  const r = await browser.runtime.sendMessage({ type: "packList" }).catch(() => null);
  if (!r || !r.ok) return;
  $("packs").replaceChildren(...r.data.map(p => {
    const busy = p.progress !== null;
    const li = document.createElement("li");
    const text = document.createElement("div");
    const name = document.createElement("b");
    name.textContent = t("lang." + p.lang);
    const info = document.createElement("small");
    info.textContent = t("o.packInfo", { words: p.words, mb: num(p.bytes / 1e6, { maximumFractionDigits: 1 }) });
    text.append(name, info);
    if (packErrors[p.lang]) {
      const err = document.createElement("small");
      err.className = "err";
      err.textContent = t(packErrors[p.lang] === "pack_bad" ? "o.packBad" : "o.packFailed");
      text.append(err);
    }
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "btn small" + (p.installed ? " danger" : "");
    btn.disabled = busy;
    btn.textContent = busy ? t("o.packGetting", { p: Math.round(p.progress * 100) }) : p.installed ? t("common.remove") : t("o.packGet");
    btn.addEventListener("click", async () => {
      delete packErrors[p.lang];
      const done = browser.runtime.sendMessage({ type: p.installed ? "packRemove" : "packInstall", lang: p.lang });
      if (!p.installed) setTimeout(renderPacks, 100); // shows the progress while it downloads
      const res = await done.catch(() => ({ ok: false, error: "pack_download" }));
      if (!res || !res.ok) packErrors[p.lang] = (res && res.error) || "pack_download";
      renderPacks();
    });
    li.append(text, btn);
    return li;
  }));
  if (r.data.some(p => p.progress !== null)) packTimer = setTimeout(renderPacks, 300);
}

browser.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.explainLangs) renderExplainLangs(changes.explainLangs.newValue);
  if (area === "sync" && changes.enDict) $("enDict").checked = !!changes.enDict.newValue;
  if (area === "sync" && changes.disabledSites) renderSites(changes.disabledSites.newValue || []);
  if (area === "local" && changes.mistakes) renderJournal();
  if (area === "local" && changes.cards) renderReviewStats();
  if (area === "sync" && changes.cardsNewPerDay) renderReviewStats();
});

init();
