/* Lamha desktop — Settings → الحافظة and التحديثات. The settings window shows the extension's options.html, which
 * this app doesn't modify: main.js injects this script to add the sections, built from the page's own components
 * (.panel, .opt, .switch, select, .sites). Values live in storage.local (read by main.js). */
"use strict";
(() => {
  if (document.getElementById("clipPanel") || !document.getElementById("review")) return;
  const { h, ask, arNum, appName } = LamhaClipList;
  const DEFAULT_APPS = ["keepass.exe", "keepassxc.exe", "1password.exe", "bitwarden.exe", "enpass.exe", "dashlane.exe", "nordpass.exe"];
  const DEFAULTS = { clipboardEnabled: false, clipboardMaxItems: 500, clipboardExpiryDays: 30, clipboardSkipCards: true, clipboardExcludedApps: DEFAULT_APPS };
  const local = browser.storage.local;

  const sw = (id, label) => h("span", { class: "switch" }, h("input", { type: "checkbox", id, "aria-label": label }), h("span", { class: "track" }, h("span", { class: "thumb" })));
  const select = (id, label, options) => h("select", { id, "aria-label": label }, options.map(([v, t]) => h("option", { value: String(v) }, t)));

  const enc = h("p", { class: "muted", id: "cbEnc", role: "status" });
  const appsList = h("ul", { class: "sites", id: "cbApps", "aria-label": "البرامج المستثناة" });
  const appInput = h("input", { type: "text", id: "cbAppInput", dir: "ltr", placeholder: "program.exe", "aria-label": "اسم البرنامج (exe)", autocomplete: "off" });
  const appMsg = h("span", { class: "cb-set-msg", role: "status" });
  const seen = h("div", { class: "cb-seen", id: "cbSeen" });

  const panel = h("section", { class: "panel", id: "clipPanel" },
    h("h2", null, "الحافظة 📋"),
    h("p", { class: "muted" }, "سجل لما تنسخه في أي برنامج، تفتحه بـ ", h("span", { class: "combo" }, h("kbd", null, "Alt"), "+", h("kbd", null, "Shift"), "+", h("kbd", null, "V")),
      " لتبحث فيه وتلصق. يبقى على جهازك فقط."),
    enc,
    h("label", { class: "opt" },
      h("div", null, h("b", null, "تفعيل سجل الحافظة"), h("small", null, "متوقف افتراضيًا. لا يُرسل أي نص إلا عندما تضغط إحدى أدوات لمحة عليه.")),
      sw("clipboardEnabled", "تفعيل سجل الحافظة")),
    h("div", { class: "opt" },
      h("div", null, h("b", null, "عدد العناصر المحفوظة"), h("small", null, "تُحذف الأقدم عند تجاوزه. المثبّتة لا تُحسب.")),
      select("clipboardMaxItems", "عدد العناصر المحفوظة", [100, 250, 500, 1000].map(n => [n, arNum(n)]))),
    h("div", { class: "opt" },
      h("div", null, h("b", null, "الحذف التلقائي لغير المثبّت بعد"), h("small", null, "منذ آخر نسخ أو استخدام")),
      select("clipboardExpiryDays", "الحذف التلقائي لغير المثبّت بعد", [[1, "يوم"], [7, "٧ أيام"], [30, "٣٠ يومًا"], [90, "٩٠ يومًا"], [0, "أبدًا"]])),
    h("label", { class: "opt" },
      h("div", null, h("b", null, "تجاهل أرقام البطاقات البنكية"), h("small", null, "لا يُحفظ رقم بطاقة (١٣–١٩ رقمًا) منسوخ وحده.")),
      sw("clipboardSkipCards", "تجاهل أرقام البطاقات البنكية")),
    h("h3", { class: "sub" }, "البرامج المستثناة"),
    h("p", { class: "muted" }, "لا يُسجَّل شيء تنسخه من هذه البرامج. مديرو كلمات المرور مستثنون من البداية."),
    appsList,
    h("form", { class: "key-row", id: "cbAppForm" }, appInput, h("button", { class: "btn small", type: "submit" }, "إضافة"), appMsg),
    seen,
    h("div", { class: "opt" },
      h("div", null, h("b", null, "مسح السجل"), h("small", null, "لا يمكن التراجع عن المسح")),
      h("div", { class: "cb-set-acts" },
        h("button", { class: "btn small ghost", type: "button", id: "cbClearUnpinned" }, "مسح غير المثبّت"),
        h("button", { class: "btn small ghost lc-danger-text", type: "button", id: "cbClearAll" }, "مسح الكل")))
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
    enc.textContent = encrypted ? "🔒 السجل مشفّر على هذا الجهاز" : "⚠️ التشفير غير متاح على هذا الجهاز — يُحفظ السجل دون تشفير.";
    renderSeen(s.clipboardExcludedApps);
  }

  function renderApps(list) {
    if (!list.length) {
      appsList.replaceChildren(h("li", { class: "empty" }, "لا توجد برامج مستثناة."));
      return;
    }
    appsList.replaceChildren(...list.map(exe => h("li", null,
      h("span", null, exe),
      h("button", { class: "link", type: "button", "aria-label": "إزالة " + exe, onclick: async () => { await lamhaClipboard.includeApp(exe); load(); } }, "إزالة"))));
  }

  /** Programs in the history that aren't excluded: one click adds them. */
  async function renderSeen(excluded) {
    const r = await lamhaClipboard.apps();
    const apps = (r.ok ? r.data : []).filter(a => a.app !== "lamha" && a.app !== "unknown" && !excluded.includes(a.app)).slice(0, 12);
    seen.replaceChildren(...(apps.length ? [
      h("span", { class: "muted" }, "من سجلك:"),
      ...apps.map(a => h("button", { class: "lc-chip", type: "button", title: `${arNum(a.count)} عنصر`, onclick: () => add(a.app) }, "+ " + appName(a.app)))
    ] : []));
  }

  async function add(exe) {
    const r = await lamhaClipboard.excludeApp(exe);
    appMsg.textContent = r.ok ? "" : "اكتب اسم البرنامج كما يظهر في مدير المهام، مثل notepad.exe";
    if (!r.ok) return;
    appInput.value = "";
    await load();
    const has = await lamhaClipboard.apps();
    const count = ((has.ok ? has.data : []).find(a => a.app === r.data) || {}).count;
    if (count && await ask(`حذف ما نُسخ من ${appName(r.data)}؟`, `في السجل ${arNum(count)} عنصر من هذا البرنامج.`,
      [{ label: "حذف", value: true, danger: true }, { label: "الإبقاء عليها", value: false }])) {
      await lamhaClipboard.removeApp(r.data);
      load();
    }
  }

  $("clipboardEnabled").addEventListener("change", async e => {
    if (e.target.checked) { await lamhaClipboard.setEnabled(true); return; }
    const choice = await ask("إيقاف سجل الحافظة", "هل تريد حذف السجل الحالي أيضًا؟",
      [{ label: "حذف", value: "delete", danger: true }, { label: "الإبقاء عليه", value: "keep" }]);
    if (!choice) { e.target.checked = true; return; } // Esc: nothing changes
    await lamhaClipboard.setEnabled(false, choice === "delete");
  });
  for (const id of ["clipboardMaxItems", "clipboardExpiryDays"]) {
    $(id).addEventListener("change", e => local.set({ [id]: Number(e.target.value) }));
  }
  $("clipboardSkipCards").addEventListener("change", e => local.set({ clipboardSkipCards: e.target.checked }));
  $("cbAppForm").addEventListener("submit", e => { e.preventDefault(); if (appInput.value.trim()) add(appInput.value); });
  $("cbClearUnpinned").addEventListener("click", async () => {
    if (await ask("مسح غير المثبّت؟", "تبقى العناصر المثبّتة 📌 فقط.", [{ label: "مسح", value: true, danger: true }, { label: "إلغاء", value: false }])) {
      await lamhaClipboard.clear({ keepPinned: true });
      load();
    }
  });
  $("cbClearAll").addEventListener("click", async () => {
    if (await ask("مسح كل السجل؟", "يُحذف كل ما في الحافظة، بما في ذلك العناصر المثبّتة.", [{ label: "مسح الكل", value: true, danger: true }, { label: "إلغاء", value: false }])) {
      await lamhaClipboard.clear({ keepPinned: false });
      load();
    }
  });
  browser.storage.onChanged.addListener((changes, area) => {
    if (area === "local" && Object.keys(changes).some(k => k in DEFAULTS)) load();
  });

  load();

  /* ---- التحديثات: from the GitHub Releases (updater.js) ---- */
  if (window.lamhaUpdates) {
    const upStatus = h("span", { class: "cb-up-status", role: "status" });
    const upBtn = h("button", { class: "btn small", type: "button", id: "upCheck" }, "التحقق الآن");
    const upPanel = h("section", { class: "panel", id: "updatesPanel" },
      h("h2", null, "التحديثات"),
      h("p", { class: "muted", id: "upVersion" }),
      h("label", { class: "opt" },
        h("div", null, h("b", null, "التحديثات التلقائية"), h("small", null, "يتحقق لمحة من وجود إصدار جديد عند التشغيل وكل ٦ ساعات، وينزّله في الخلفية، ويثبّته عند إعادة التشغيل.")),
        sw("updatesAuto", "التحديثات التلقائية")),
      h("div", { class: "opt" },
        h("div", null, h("b", null, "التحقق من وجود تحديث"), upStatus),
        upBtn),
      h("button", { class: "link", type: "button", id: "upReleases" }, "ما الجديد في كل إصدار")
    );
    panel.after(upPanel);
    const STATUS = {
      checking: () => "جارٍ التحقق…",
      downloading: st => `يُنزَّل الإصدار ${st.version}…`,
      ready: st => `الإصدار ${st.version} جاهز — أعد التشغيل لتثبيته`,
      available: st => `الإصدار ${st.version} متاح — نسخة Portable تُحدَّث بتنزيل الإصدار الجديد`,
      latest: () => "لديك أحدث إصدار ✓",
      error: () => "تعذّر التحقق من التحديثات. تحقق من اتصالك.",
      dev: () => "التحديث التلقائي يعمل في النسخة المثبّتة فقط."
    };
    let releases = "";
    const renderUpdates = async () => {
      const st = await lamhaUpdates.state();
      releases = st.releases;
      $("upVersion").textContent = `الإصدار الحالي: ${st.current}` + (st.portable ? " (Portable)" : "");
      upStatus.textContent = STATUS[st.status] ? STATUS[st.status](st) : "";
      upBtn.textContent = st.status === "ready" ? "أعد التشغيل الآن" : st.status === "available" ? "تنزيل" : "التحقق الآن";
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

  const toSection = () => {
    const target = { "#clipboard": panel, "#updates": document.getElementById("updatesPanel") }[location.hash];
    if (target) target.scrollIntoView({ block: "start" });
  };
  window.addEventListener("hashchange", toSection); // Settings already open: only the hash changes
  toSection();
})();
