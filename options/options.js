"use strict";

const DEFAULTS = {
  enabled: true, targetLang: "ar", triggerMode: "button", reverseForArabic: true, dictSource: "local", useContext: true,
  showInInputs: false, showWikipedia: true, translateDefinitions: true, autoSpeak: false,
  theme: "auto", saveHistory: true, aiModel: "claude-opus-5", aiInInputs: true, saveMistakes: true, cardsAuto: true, cardsNewPerDay: 10, disabledSites: []
};
const BOOLS = ["useContext", "reverseForArabic", "translateDefinitions", "showWikipedia", "autoSpeak", "showInInputs", "saveHistory", "aiInInputs", "saveMistakes", "cardsAuto"];
const AI_ERRORS = {
  ai_bad_key: "المفتاح غير صالح. تأكد من نسخه كاملًا.",
  ai_no_credit: "المفتاح صحيح لكن رصيد الحساب نفد. أضف رصيدًا من console.anthropic.com.",
  ai_forbidden: "المفتاح لا يملك صلاحية استخدام الـ API.",
  ai_model: "النموذج المختار غير متاح لهذا الحساب. جرّب نموذجًا آخر.",
  ai_rate_limited: "طلبات كثيرة، انتظر دقيقة ثم أعد المحاولة.",
  ai_busy: "خدمة الذكاء الاصطناعي مشغولة الآن، أعد المحاولة بعد لحظات.",
  ai_timeout: "انتهت مهلة الاتصال، أعد المحاولة.",
  network: "تعذّر الاتصال بالإنترنت.",
  ollama_offline: "Ollama لا يعمل على هذا العنوان. شغّله من قائمة ابدأ (أو ثبّته من ollama.com).",
  ollama_origin: "Ollama يرفض اتصال الإضافة: نفّذ أمر OLLAMA_ORIGINS في الخطوة 2 ثم أعد تشغيل Ollama.",
  ollama_model: "النموذج غير موجود. حمّله بالأمر ollama pull ثم اضغط «تحديث القائمة»."
};
const aiErrorText = code => AI_ERRORS[code]
  || (LamhaAI.ERRORS[code] && LamhaAI.ERRORS[code].slice(0, 2).join(" — "))
  || "تعذّر الاتصال: " + String(code || "").replace(/^ai_error:/, "");
const $ = id => document.getElementById(id);

let savedTimer;
function saved() {
  const el = $("saved");
  el.classList.add("show");
  clearTimeout(savedTimer);
  savedTimer = setTimeout(() => el.classList.remove("show"), 1200);
}
const save = patch => browser.storage.sync.set(patch).then(saved);

function kbd(combo) {
  return (combo || "—").split("+").map(k => {
    const el = document.createElement("kbd");
    el.textContent = k;
    return el;
  });
}

async function init() {
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
    const n = x => x.toLocaleString("ar-EG");
    $("dictInfo").textContent = `القاموس المحلي: ${n(m.entries)} كلمة إنجليزية، منها ${n(m.withArabic)} بمعانٍ عربية، و${n(m.arabicIndex)} كلمة عربية — يعمل دون إنترنت.`;
  }).catch(() => {});

  BOOLS.forEach(k => {
    $(k).checked = !!s[k];
    $(k).addEventListener("change", e => save({ [k]: e.target.checked }));
  });
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
    if (!confirm("حذف كل بطاقات المراجعة وتقدّمك في حفظها؟")) return;
    await browser.storage.local.set({ cards: {}, cardStats: {}, cardsImported: true });
    saved();
  });
  $("jClear").addEventListener("click", async () => {
    await browser.storage.local.remove("mistakes");
    saved();
  });
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
    await browser.storage.local.set({ history: [] });
    renderHistCount(); saved();
  });
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
  $("rvSummary").textContent = `في قائمة مراجعتك ${arNum(total)} كلمة، حفظتَ منها ${arNum(learned)}. ينتظرك اليوم ${arNum(due)} للمراجعة و${arNum(fresh)} جديدة — افتح تبويب «مراجعة» في نافذة لمحة.`;
}

/* ---- mistake journal ---- */

let journalCat = ""; // category whose tip and mistakes are shown ("" = all recent)
const arNum = n => n.toLocaleString("ar-EG");

function el(tag, cls, ...kids) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  e.append(...kids.filter(k => k != null && k !== false));
  return e;
}

async function renderJournal() {
  const { mistakes } = await browser.storage.local.get("mistakes");
  const j = mistakes || { checks: 0, counts: {}, recent: [] };
  const cats = LamhaAI.CATEGORIES;
  const total = Object.values(j.counts).reduce((a, b) => a + b, 0);
  const rows = Object.entries(j.counts).filter(([c, n]) => cats[c] && n > 0).sort((a, b) => b[1] - a[1]);

  if (!j.checks) {
    $("jSummary").textContent = "يُسجَّل هنا ما يجده «التدقيق اللغوي» في كتابتك، لتعرف نقاط ضعفك وتتحسّن. لم تدقّق أي نص بعد.";
  } else {
    const top = rows[0] && rows[0][0] !== "other" ? ` أكثر أخطائك: ${cats[rows[0][0]].ar}.` : "";
    $("jSummary").textContent = `دقّقت ${arNum(j.checks)} نصًّا ووُجد فيها ${arNum(total)} خطأ.${top} اضغط نوعًا لترى قاعدته وأمثلة من كتابتك.`;
  }

  if (journalCat && !j.counts[journalCat]) journalCat = "";
  const max = rows.length ? rows[0][1] : 1;
  $("jBars").replaceChildren(...rows.map(([c, n]) => {
    const fill = el("span", "fill");
    fill.style.width = Math.max(4, Math.round((n / max) * 100)) + "%";
    const b = el("button", "bar" + (c === journalCat ? " on" : ""), el("span", null, cats[c].ar), el("span", "meter", fill), el("span", "n", arNum(n)));
    b.setAttribute("aria-pressed", String(c === journalCat));
    b.addEventListener("click", () => { journalCat = journalCat === c ? "" : c; renderJournal(); });
    return b;
  }));

  const tip = journalCat && cats[journalCat].tip;
  $("jTip").hidden = !tip;
  if (tip) $("jTip").replaceChildren(el("b", null, "القاعدة: "), tip);

  const list = j.recent.filter(r => !journalCat || r.cat === journalCat).slice(0, 30);
  $("jRecentBox").hidden = !list.length;
  $("jRecentTitle").textContent = journalCat ? `أمثلة من كتابتك — ${cats[journalCat].ar}` : "آخر الأخطاء";
  $("jRecent").replaceChildren(...list.map(r => el("li", null,
    el("div", "fix", el("del", null, r.original), " → ", el("ins", null, r.fix)),
    r.why && el("div", "why", r.why)
  )));
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
  setStatus("ollamaStatus", "جارٍ الاتصال بـ Ollama…");
  const res = await bg({ type: "ollamaModels", url: $("ollamaUrl").value.trim() });
  const sel = $("ollamaModel");
  if (!res || !res.ok) { setStatus("ollamaStatus", aiErrorText(res && res.error), "bad"); return; }
  const { ollamaModel = "" } = await browser.storage.local.get("ollamaModel");
  const models = res.data;
  sel.replaceChildren(...[{ name: "", size: 0 }, ...models].map(m => {
    const o = document.createElement("option");
    o.value = m.name;
    o.textContent = m.name ? `${m.name} (${(m.size / 1e9).toFixed(1)} GB)` : "— اختر نموذجًا —";
    return o;
  }));
  if (!models.length) {
    setStatus("ollamaStatus", "Ollama يعمل لكن لا توجد نماذج بعد — نفّذ الأمر في الخطوة 3 ثم اضغط «تحديث القائمة».", "bad");
    return;
  }
  let pick = models.some(m => m.name === ollamaModel) ? ollamaModel : "";
  if (!pick) { // first time: prefer a Qwen model (strong Arabic), else the first one
    pick = (models.find(m => /^qwen/i.test(m.name)) || models[0]).name;
    await browser.storage.local.set({ ollamaModel: pick });
  }
  sel.value = pick;
  setStatus("ollamaStatus", `✓ Ollama متصل — ${models.length} نموذج. اضغط «اختبار» للتأكد من أن الإضافة تستطيع استخدامه.`, "ok");
}

async function testOllama() {
  const model = $("ollamaModel").value;
  if (!model) { setStatus("ollamaStatus", "اختر نموذجًا أولًا.", "bad"); return; }
  $("ollamaTest").disabled = true;
  setStatus("ollamaStatus", `جارٍ اختبار ${model}… (أول تشغيل قد يستغرق دقيقة لتحميل النموذج في الذاكرة)`);
  const t0 = performance.now();
  const res = await bg({ type: "aiTest", provider: "ollama", model, url: $("ollamaUrl").value.trim() });
  $("ollamaTest").disabled = false;
  if (!res || !res.ok) { setStatus("ollamaStatus", aiErrorText(res && res.error), "bad"); return; }
  const secs = ((performance.now() - t0) / 1000).toLocaleString("ar-EG", { maximumFractionDigits: 1 });
  setStatus("ollamaStatus", `✓ يعمل (${secs} ث) — أدوات الكتابة جاهزة مجانًا على جهازك.`, "ok");
}

/** An API-key box (Claude or Gemini): checked with a tiny request, then kept in storage.local. */
function initKeyBox({ provider, storageKey, input, save, remove, status: statusId, model, placeholder }, hasKey) {
  const status = (text, cls) => setStatus(statusId, text, cls);
  const showSaved = has => {
    $(input).value = "";
    $(input).placeholder = has ? "•••••••• (محفوظ) — الصق مفتاحًا جديدًا لتغييره" : placeholder;
    $(remove).hidden = !has;
    status(has ? "✓ المفتاح محفوظ." : "لم يُضف مفتاح بعد.", has ? "ok" : "");
  };
  showSaved(hasKey);

  $(save).addEventListener("click", async () => {
    const key = $(input).value.trim();
    if (!key) { $(input).focus(); return; }
    $(save).disabled = true;
    status("جارٍ التحقق من المفتاح…");
    const res = await bg({ type: "aiTest", provider, key, model: $(model).value });
    $(save).disabled = false;
    if (!res || !res.ok) {
      // the service's own words help when something unexpected happens
      status(aiErrorText(res && res.error) + (res && res.detail ? `  (${res.detail})` : ""), "bad");
      return;
    }
    await browser.storage.local.set({ [storageKey]: key });
    showSaved(true);
    if (res.note === "gemini_busy") status("✓ المفتاح صحيح وحُفظ — لكن نماذج Gemini مزدحمة الآن، فجرّب أدوات الكتابة بعد قليل.", "ok");
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
    li.textContent = "لا توجد مواقع مستثناة.";
    ul.replaceChildren(li);
    return;
  }
  ul.replaceChildren(...list.map(site => {
    const li = document.createElement("li");
    const name = document.createElement("span");
    name.textContent = site;
    const btn = document.createElement("button");
    btn.className = "link";
    btn.textContent = "إزالة";
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
  $("histCount").textContent = history.length ? `${history.length} كلمة محفوظة` : "السجل فارغ";
}

browser.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.disabledSites) renderSites(changes.disabledSites.newValue || []);
  if (area === "local" && changes.mistakes) renderJournal();
  if (area === "local" && changes.cards) renderReviewStats();
  if (area === "sync" && changes.cardsNewPerDay) renderReviewStats();
});

init();
