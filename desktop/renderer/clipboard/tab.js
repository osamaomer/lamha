/* Lamha desktop — the الحافظة tab in the main window. The window shows the extension's popup.html, which this app
 * doesn't modify: main.js injects this script (and clip-list.js, clipboard.css) to add a fourth tab next to مراجعة.
 * The tab runs its own show / hide, since popup.js only knows its three tabs. */
"use strict";
(async () => {
  await LamhaI18n.init(); // popup.js redraws the whole page when the language changes
  const tabs = document.querySelector(".tabs");
  if (!tabs || document.getElementById("tabCb")) return;
  const { h, appName, arNum, ask } = LamhaClipList;
  const L = (key, vars) => LamhaI18n.t(key, vars);
  const byId = id => document.getElementById(id);
  const OTHERS = [["tabTr", "trPane"], ["tabWr", "wrPane"], ["tabRv", "rvPane"]];

  const tabBtn = h("button", { id: "tabCb", role: "tab", type: "button", "aria-selected": "false", "aria-controls": "cbPane" }, L("d.tab"));
  tabs.append(tabBtn);
  const pane = h("div", { id: "cbPane", role: "tabpanel", "aria-labelledby": "tabCb", hidden: true });
  byId("rvPane").after(pane);

  const listRoot = h("div");
  const settingsLink = h("button", {
    class: "link cb-settings", type: "button",
    onclick: () => browser.tabs.create({ url: browser.runtime.getURL("options/options.html#clipboard") })
  }, L("d.settingsLink"));
  /** مسح غير المثبّت: empties the history in one step; pinned 📌 items stay. */
  const clearBtn = h("button", { class: "btn small ghost lc-danger-text", type: "button", id: "cbClear" }, L("d.clearUnpinned"));
  const foot = h("div", { class: "cb-foot" }, clearBtn, settingsLink);
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
    if (!pane.contains(listRoot)) pane.replaceChildren(listRoot, detail, foot);
    updateClear();
    if (openId) return openDetail(openId);
    await list.reload();
    if (focus) list.focus();
  }

  /** How many would go: shown on the button, which is off when there is nothing to clear. */
  async function updateClear() {
    const [all, pinned] = await Promise.all([lamhaClipboard.list({ limit: 0 }), lamhaClipboard.list({ filter: "pinned", limit: 0 })]);
    const n = Math.max(0, (all.ok ? all.data.total : 0) - (pinned.ok ? pinned.data.total : 0));
    clearBtn.dataset.n = String(n);
    clearBtn.disabled = n === 0;
    clearBtn.textContent = n ? L("d.clearUnpinnedN", { n }) : L("d.clearUnpinned");
    clearBtn.title = n ? L("d.clearUnpinnedTitle") : L("d.nothingUnpinned");
  }

  clearBtn.addEventListener("click", async () => {
    const n = Number(clearBtn.dataset.n) || 0;
    if (!n) return;
    const ok = await ask(L("d.clearUnpinnedQ"), L("d.clearUnpinnedQText", { n }),
      [{ label: L("d.clear"), value: true, danger: true }, { label: L("d.cancel"), value: false }]);
    if (!ok) { clearBtn.focus(); return; }
    const r = await lamhaClipboard.clear({ keepPinned: true });
    if (!r.ok) { flash(L("d.clearFailed")); return; }
    if (openId) back();
    list.toast(L("d.clearedN", { n }));
    updateClear();
    list.focus();
  });

  const flash = text => {
    const t = h("div", { class: "flash", role: "status" }, text);
    document.body.append(t);
    setTimeout(() => t.remove(), 1400);
  };
  const when = ts => (ts ? new Date(ts).toLocaleString(LamhaI18n.lang() === "ar" ? "ar-EG" : "en-US", { dateStyle: "medium", timeStyle: "short" }) : "—");

  async function openDetail(id) {
    const r = await lamhaClipboard.get(id);
    if (!r.ok || !r.data) { back(); return; }
    const c = r.data;
    const first = openId !== id;
    openId = id;
    listRoot.hidden = true;
    detail.hidden = false;
    clearBtn.hidden = true; // clearing belongs to the list, not to one item

    const labelEdit = h("form", { class: "cb-labeledit", hidden: true });
    const labelInput = h("input", { type: "text", maxlength: "60", dir: "auto", "aria-label": L("d.labelInput"), placeholder: L("d.labelPlaceholder") });
    labelInput.value = c.label;
    labelEdit.append(labelInput, h("button", { class: "btn small", type: "submit" }, L("d.save")), h("button", { class: "btn small ghost", type: "button", onclick: () => { labelEdit.hidden = true; } }, L("d.cancel")));
    labelEdit.addEventListener("submit", async e => {
      e.preventDefault();
      await lamhaClipboard.setLabel(id, labelInput.value);
      flash(L("d.labelSaved"));
    });
    labelInput.addEventListener("keydown", e => { if (e.key === "Escape") { e.stopPropagation(); labelEdit.hidden = true; } });

    const backBtn = h("button", { class: "link", type: "button", onclick: back }, L("d.backToList"));
    if (toolsFor !== id) {
      tools = h("section", { class: "cb-tools", "aria-label": L("d.toolsLabel") });
      LamhaClipActions.mount(tools, c, { mode: "tab" });
      toolsFor = id;
    }
    detail.replaceChildren(
      h("div", { class: "cb-dhead" }, backBtn, h("span", { class: "grow" }), c.pinned && h("span", { title: L("d.pinned"), "aria-label": L("d.pinned") }, "📌")),
      c.label && h("h3", { class: "cb-dlabel", dir: "auto" }, c.label),
      h("pre", { class: "cb-full", dir: "auto", tabindex: "0", "aria-label": L("d.fullText") }, c.text),
      h("dl", { class: "cb-facts" },
        h("dt", null, L("d.program")), h("dd", { dir: "auto" }, appName(c.sourceApp)),
        h("dt", null, L("d.firstCopied")), h("dd", null, when(c.createdAt)),
        h("dt", null, L("d.lastCopied")), h("dd", null, when(c.lastCopiedAt)),
        h("dt", null, L("d.timesCopied")), h("dd", null, arNum(c.copyCount))
      ),
      h("div", { class: "cb-acts" },
        h("button", { class: "btn small", type: "button", onclick: async () => { const x = await lamhaClipboard.copy(id); flash(x.ok ? L("d.copied") : L("d.copyFailed")); } }, L("common.copy")),
        h("button", {
          class: "btn small ghost", type: "button", "aria-pressed": String(c.pinned),
          onclick: async () => {
            const x = await lamhaClipboard.setPinned(id, !c.pinned);
            if (!x.ok) flash(x.error === "pin_limit" ? L("d.pinLimit") : L("d.pinFailed"));
          }
        }, c.pinned ? L("d.unpin") : L("d.pin")),
        h("button", { class: "btn small ghost", type: "button", onclick: () => { labelEdit.hidden = false; labelInput.focus(); labelInput.select(); } }, L("d.label")),
        h("button", {
          class: "btn small ghost danger", type: "button",
          onclick: async () => {
            const x = await lamhaClipboard.remove(id);
            if (!x.ok) return;
            back();
            list.toast(L("d.deleted"), { label: L("d.undo"), run: () => lamhaClipboard.restore(id) });
          }
        }, L("d.delete")),
        !["lamha", "unknown"].includes(c.sourceApp) && h("button", {
          class: "btn small ghost cb-ignore", type: "button", title: L("d.ignoreAppTitle"),
          onclick: () => ignoreApp(c.sourceApp)
        }, L("d.ignoreApp"))
      ),
      labelEdit,
      tools
    );
    if (first) backBtn.focus();
  }

  /** تجاهل هذا البرنامج: add it to the excluded programs, then offer to delete what was already copied from it. */
  async function ignoreApp(exe) {
    const r = await lamhaClipboard.excludeApp(exe);
    if (!r.ok) { flash(L("d.failed")); return; }
    const apps = await lamhaClipboard.apps();
    const count = ((apps.ok ? apps.data : []).find(a => a.app === r.data) || {}).count || 0;
    const del = await ask(L("d.ignoredQ", { app: appName(r.data) }), L("d.ignoredQText", { n: count }),
      [{ label: L("d.delete"), value: true, danger: true }, { label: L("d.keepThem"), value: false }]);
    if (del) {
      await lamhaClipboard.removeApp(r.data);
      back();
    }
    flash(L("d.ignoredDone", { app: appName(r.data) }));
  }

  function back() {
    openId = null;
    toolsFor = null;
    clearBtn.hidden = false;
    detail.hidden = true;
    listRoot.hidden = false;
    list.reload();
    list.focus();
  }

  detail.addEventListener("keydown", e => { if (e.key === "Escape") { e.preventDefault(); back(); } });
  // live: new copies, pins and labels from the quick panel
  lamhaClipboard.onChanged(() => {
    if (!pane.hidden) updateClear();
    const typing = detail.contains(document.activeElement) && document.activeElement.tagName === "INPUT";
    if (!pane.hidden && openId && !typing) openDetail(openId);
  });
  browser.storage.onChanged.addListener((changes, area) => { if (area === "local" && changes.clipboardEnabled && !pane.hidden) render(); });
})();
