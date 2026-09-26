/* Lamha desktop — the الحافظة tab in the main window. The window shows the extension's popup.html, which this app
 * doesn't modify: main.js injects this script (and clip-list.js, clipboard.css) to add a fourth tab next to مراجعة.
 * The tab runs its own show / hide, since popup.js only knows its three tabs. */
"use strict";
(() => {
  const tabs = document.querySelector(".tabs");
  if (!tabs || document.getElementById("tabCb")) return;
  const { h, appName, arNum, ask } = LamhaClipList;
  const byId = id => document.getElementById(id);
  const OTHERS = [["tabTr", "trPane"], ["tabWr", "wrPane"], ["tabRv", "rvPane"]];

  const tabBtn = h("button", { id: "tabCb", role: "tab", type: "button", "aria-selected": "false", "aria-controls": "cbPane" }, "الحافظة");
  tabs.append(tabBtn);
  const pane = h("div", { id: "cbPane", role: "tabpanel", "aria-labelledby": "tabCb", hidden: true });
  byId("rvPane").after(pane);

  const listRoot = h("div");
  const settingsLink = h("button", {
    class: "link cb-settings", type: "button",
    onclick: () => browser.tabs.create({ url: browser.runtime.getURL("options/options.html#clipboard") })
  }, "إعدادات الحافظة والخصوصية");
  const detail = h("div", { class: "cb-detail", hidden: true });
  const optIn = LamhaClipList.optIn(() => render());
  const list = LamhaClipList.create({ root: listRoot, mode: "tab", onActivate: it => openDetail(it.id) });
  let openId = null;
  let tools = null, toolsFor = null; // kept across re-renders of the same clip, so a result stays visible

  tabBtn.addEventListener("click", select);
  for (const [t] of OTHERS) byId(t).addEventListener("click", () => {
    tabBtn.setAttribute("aria-selected", "false");
    pane.hidden = true;
    document.body.classList.remove("cb-on");
  });

  function select() {
    for (const [t, p] of OTHERS) { byId(t).setAttribute("aria-selected", "false"); byId(p).hidden = true; }
    tabBtn.setAttribute("aria-selected", "true");
    pane.hidden = false;
    document.body.classList.add("cb-on");
    render(true);
  }

  async function render(focus) {
    const r = await lamhaClipboard.status();
    const enabled = r.ok && r.data.enabled;
    if (!enabled) {
      openId = null;
      pane.replaceChildren(optIn);
      return;
    }
    if (!pane.contains(listRoot)) pane.replaceChildren(listRoot, detail, settingsLink);
    if (openId) return openDetail(openId);
    await list.reload();
    if (focus) list.focus();
  }

  const flash = text => {
    const t = h("div", { class: "flash", role: "status" }, text);
    document.body.append(t);
    setTimeout(() => t.remove(), 1400);
  };
  const when = ts => (ts ? new Date(ts).toLocaleString("ar-EG", { dateStyle: "medium", timeStyle: "short" }) : "—");

  async function openDetail(id) {
    const r = await lamhaClipboard.get(id);
    if (!r.ok || !r.data) { back(); return; }
    const c = r.data;
    const first = openId !== id;
    openId = id;
    listRoot.hidden = true;
    detail.hidden = false;

    const labelEdit = h("form", { class: "cb-labeledit", hidden: true });
    const labelInput = h("input", { type: "text", maxlength: "60", dir: "auto", "aria-label": "تسمية العنصر", placeholder: "اسم قصير يسهل البحث عنه" });
    labelInput.value = c.label;
    labelEdit.append(labelInput, h("button", { class: "btn small", type: "submit" }, "حفظ"), h("button", { class: "btn small ghost", type: "button", onclick: () => { labelEdit.hidden = true; } }, "إلغاء"));
    labelEdit.addEventListener("submit", async e => {
      e.preventDefault();
      await lamhaClipboard.setLabel(id, labelInput.value);
      flash("حُفظت التسمية");
    });
    labelInput.addEventListener("keydown", e => { if (e.key === "Escape") { e.stopPropagation(); labelEdit.hidden = true; } });

    const backBtn = h("button", { class: "link", type: "button", onclick: back }, "→ رجوع إلى القائمة");
    if (toolsFor !== id) {
      tools = h("section", { class: "cb-tools", "aria-label": "أدوات لمحة" });
      LamhaClipActions.mount(tools, c, { mode: "tab" });
      toolsFor = id;
    }
    detail.replaceChildren(
      h("div", { class: "cb-dhead" }, backBtn, h("span", { class: "grow" }), c.pinned && h("span", { title: "مثبّت", "aria-label": "مثبّت" }, "📌")),
      c.label && h("h3", { class: "cb-dlabel", dir: "auto" }, c.label),
      h("pre", { class: "cb-full", dir: "auto", tabindex: "0", "aria-label": "النص كاملًا" }, c.text),
      h("dl", { class: "cb-facts" },
        h("dt", null, "البرنامج"), h("dd", { dir: "auto" }, appName(c.sourceApp)),
        h("dt", null, "أول نسخ"), h("dd", null, when(c.createdAt)),
        h("dt", null, "آخر نسخ"), h("dd", null, when(c.lastCopiedAt)),
        h("dt", null, "مرات النسخ"), h("dd", null, arNum(c.copyCount))
      ),
      h("div", { class: "cb-acts" },
        h("button", { class: "btn small", type: "button", onclick: async () => { const x = await lamhaClipboard.copy(id); flash(x.ok ? "نُسخ ✓" : "تعذّر النسخ"); } }, "نسخ"),
        h("button", {
          class: "btn small ghost", type: "button", "aria-pressed": String(c.pinned),
          onclick: async () => {
            const x = await lamhaClipboard.setPinned(id, !c.pinned);
            if (!x.ok) flash(x.error === "pin_limit" ? "وصلت إلى الحد الأقصى للعناصر المثبتة (٣٠٠)" : "تعذّر التثبيت");
          }
        }, c.pinned ? "إلغاء التثبيت" : "تثبيت"),
        h("button", { class: "btn small ghost", type: "button", onclick: () => { labelEdit.hidden = false; labelInput.focus(); labelInput.select(); } }, "تسمية"),
        h("button", {
          class: "btn small ghost danger", type: "button",
          onclick: async () => {
            const x = await lamhaClipboard.remove(id);
            if (!x.ok) return;
            back();
            list.toast("حُذف العنصر", { label: "تراجع", run: () => lamhaClipboard.restore(id) });
          }
        }, "حذف"),
        !["lamha", "unknown"].includes(c.sourceApp) && h("button", {
          class: "btn small ghost cb-ignore", type: "button", title: "لا تسجّل ما يُنسخ من هذا البرنامج بعد الآن",
          onclick: () => ignoreApp(c.sourceApp)
        }, "تجاهل هذا البرنامج")
      ),
      labelEdit,
      tools
    );
    if (first) backBtn.focus();
  }

  /** تجاهل هذا البرنامج: add it to the excluded programs, then offer to delete what was already copied from it. */
  async function ignoreApp(exe) {
    const r = await lamhaClipboard.excludeApp(exe);
    if (!r.ok) { flash("تعذّر ذلك"); return; }
    const apps = await lamhaClipboard.apps();
    const count = ((apps.ok ? apps.data : []).find(a => a.app === r.data) || {}).count || 0;
    const del = await ask(`لن يُسجَّل ما تنسخه من ${appName(r.data)}`, `هل تريد حذف ما في السجل منه أيضًا (${arNum(count)})؟`,
      [{ label: "حذف", value: true, danger: true }, { label: "الإبقاء عليها", value: false }]);
    if (del) {
      await lamhaClipboard.removeApp(r.data);
      back();
    }
    flash(`أُضيف ${appName(r.data)} إلى البرامج المستثناة`);
  }

  function back() {
    openId = null;
    toolsFor = null;
    detail.hidden = true;
    listRoot.hidden = false;
    list.reload();
    list.focus();
  }

  detail.addEventListener("keydown", e => { if (e.key === "Escape") { e.preventDefault(); back(); } });
  // live: new copies, pins and labels from the quick panel
  lamhaClipboard.onChanged(() => {
    const typing = detail.contains(document.activeElement) && document.activeElement.tagName === "INPUT";
    if (!pane.hidden && openId && !typing) openDetail(openId);
  });
  browser.storage.onChanged.addListener((changes, area) => { if (area === "local" && changes.clipboardEnabled && !pane.hidden) render(); });
})();
