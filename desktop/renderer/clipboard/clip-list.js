/* Lamha desktop — clipboard history list, shared by the quick panel (panel.js) and the الحافظة tab (tab.js).
 * Search box, الكل / المثبتة chips, rows with highlighted matches, keyboard selection, delete with undo.
 * Talks to the main process through window.lamhaClipboard (preload.js); needs LamhaArabic (shared/arabic-normalize.js). */
"use strict";
// eslint-disable-next-line no-unused-vars
var LamhaClipList = (() => {
  const PAGE = 100;
  const UNDO_MS = 5000;

  function h(tag, props, ...kids) {
    const el = document.createElement(tag);
    if (props) for (const [k, v] of Object.entries(props)) {
      if (v == null || v === false) continue;
      if (k === "class") el.className = v;
      else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
      else el.setAttribute(k, v === true ? "" : v);
    }
    kids.flat().forEach(c => c != null && c !== false && el.append(c));
    return el;
  }

  const svg = paths => {
    const s = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    for (const [k, v] of Object.entries({ viewBox: "0 0 24 24", width: 15, height: 15, fill: "none", stroke: "currentColor", "stroke-width": 2, "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true" })) s.setAttribute(k, v);
    s.innerHTML = paths; // static markup below, never clip text
    return s;
  };
  const ICONS = {
    pin: '<path d="M12 17v5"/><path d="M9 10.76V6h6v4.76l2.5 3.24h-11z"/><path d="M8 3h8"/>',
    trash: '<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M6 6l1 14h10l1-14"/>'
  };
  const icon = name => svg(ICONS[name]);

  /** Numbers in the app's convention: Arabic-Indic digits (as popup.js does with toLocaleString("ar-EG")). */
  const arNum = n => Number(n).toLocaleString("ar-EG");

  /** "قبل ٥ دقائق" — Arabic counting: 1, 2 (dual), 3–10 plural, 11+ singular with tanween. */
  function relTime(ts, now = Date.now()) {
    const s = Math.max(0, Math.round((now - ts) / 1000));
    const unit = (n, one, two, few, many) => (n === 1 ? one : n === 2 ? two : n <= 10 ? `${arNum(n)} ${few}` : `${arNum(n)} ${many}`);
    if (s < 60) return "الآن";
    const m = Math.floor(s / 60);
    if (m < 60) return "قبل " + unit(m, "دقيقة", "دقيقتين", "دقائق", "دقيقة");
    const hr = Math.floor(m / 60);
    if (hr < 24) return "قبل " + unit(hr, "ساعة", "ساعتين", "ساعات", "ساعة");
    const d = Math.floor(hr / 24);
    if (d === 1) return "أمس";
    if (d < 30) return "قبل " + unit(d, "يوم", "يومين", "أيام", "يومًا");
    return new Date(ts).toLocaleDateString("ar-EG", { day: "numeric", month: "long", year: "numeric" });
  }

  const appName = app => (app === "lamha" ? "لمحة" : app === "unknown" ? "برنامج غير معروف" : app.replace(/\.exe$/i, ""));

  /** Text with <mark> around what the query matched (same Arabic-aware rules as the search). */
  function highlight(text, query) {
    const frag = document.createDocumentFragment();
    let at = 0;
    for (const [a, b] of query ? LamhaArabic.matchRanges(text, query) : []) {
      if (a > at) frag.append(text.slice(at, a));
      frag.append(h("mark", null, text.slice(a, b)));
      at = b;
    }
    frag.append(text.slice(at));
    return frag;
  }

  /** The two lines shown in a row: leading blank lines skipped, the rest left to CSS line-clamp. */
  const rowText = t => t.replace(/^\s+/, "").slice(0, 300);

  let uid = 0;

  /**
   * @param {object} o
   * @param {HTMLElement} o.root
   * @param {"panel"|"tab"} o.mode      panel: click / Enter pastes, rows numbered 1–9. tab: click / Enter opens.
   * @param {(item, opts: { plain: boolean }) => void} o.onActivate
   * @param {(item) => void} [o.onActions]   Tab / → on a row (panel)
   * @param {() => void} [o.onEscape]
   */
  function create({ root, mode, onActivate, onActions, onEscape }) {
    const id = "lc" + ++uid;
    let query = "", filter = "all", shown = PAGE, items = [], total = 0, sel = 0, loading = null, reloadAgain = false;

    const search = h("input", {
      class: "lc-search", type: "search", dir: "auto", placeholder: "ابحث في الحافظة…", "aria-label": "ابحث في الحافظة",
      role: "combobox", "aria-controls": id + "-list", "aria-expanded": "true", "aria-autocomplete": "list", spellcheck: "false"
    });
    const chips = ["all", "pinned"].map(f => h("button", { class: "lc-chip", type: "button", "aria-pressed": String(f === filter), onclick: () => setFilter(f) }, f === "all" ? "الكل" : "المثبتة"));
    const list = h("ul", { class: "lc-list", id: id + "-list", role: "listbox", "aria-label": "سجل الحافظة" });
    const more = h("button", { class: "btn small ghost lc-more", type: "button", hidden: true, onclick: () => { shown += PAGE; reload(); } }, "عرض المزيد");
    const empty = h("p", { class: "lc-empty", hidden: true, role: "status" });
    const toastBox = h("div", { class: "lc-toasts", "aria-live": "polite" });
    root.classList.add("lc", "lc-" + mode);
    root.replaceChildren(
      h("div", { class: "lc-bar" }, search, h("div", { class: "lc-chips", role: "group", "aria-label": "تصفية" }, chips)),
      list, more, empty, toastBox
    );

    search.addEventListener("input", () => { query = search.value; shown = PAGE; sel = 0; reload(); });
    root.addEventListener("keydown", onKey);

    function setFilter(f) {
      filter = f;
      chips.forEach((c, i) => c.setAttribute("aria-pressed", String(["all", "pinned"][i] === f)));
      shown = PAGE;
      sel = 0;
      reload();
    }

    /** Fetches the current page; calls made while one is in flight collapse into one more. */
    async function reload() {
      if (loading) { reloadAgain = true; return loading; }
      loading = (async () => {
        do {
          reloadAgain = false;
          const keep = items[sel] && items[sel].id;
          const r = await lamhaClipboard.list({ query, filter, limit: shown, offset: 0 });
          if (!r.ok) break;
          items = r.data.items;
          total = r.data.total;
          const again = keep ? items.findIndex(i => i.id === keep) : -1;
          sel = again >= 0 && !query ? again : Math.min(sel, Math.max(0, items.length - 1));
          render();
        } while (reloadAgain);
      })().finally(() => { loading = null; });
      return loading;
    }

    function render() {
      list.replaceChildren(...items.map((it, i) => row(it, i)));
      more.hidden = items.length >= total;
      empty.hidden = items.length > 0;
      empty.textContent = query ? "لا نتائج"
        : filter === "pinned" ? "لا توجد عناصر مثبّتة بعد — ثبّت ما تحتاجه كثيرًا بـ Ctrl+P"
        : "لا يوجد شيء في الحافظة بعد — انسخ أي نص وسيظهر هنا";
      markSelected();
    }

    function row(it, i) {
      const meta = h("div", { class: "lc-meta" },
        mode === "panel" && i < 9 && h("kbd", { "aria-hidden": "true" }, String(i + 1)),
        h("span", { class: "lc-app", dir: "auto" }, appName(it.sourceApp)),
        h("span", { "aria-hidden": "true" }, "·"),
        h("span", null, relTime(Math.max(it.lastCopiedAt, it.lastUsedAt))),
        it.pinned && h("span", { class: "lc-pin", title: "مثبّت", "aria-label": "مثبّت" }, "📌")
      );
      const li = h("li", { class: "lc-row", role: "option", id: `${id}-o${i}`, "aria-selected": "false", "data-id": it.id },
        h("div", { class: "lc-main" },
          it.label && h("b", { class: "lc-label", dir: "auto" }, highlight(it.label, query)),
          h("div", { class: "lc-text", dir: "auto" }, highlight(rowText(it.text), query)),
          it.translation && h("div", { class: "lc-tr", dir: "rtl" }, it.translation), // cached translation of an English clip
          meta
        ),
        h("div", { class: "lc-tools" },
          h("button", { class: "icon-btn", type: "button", tabindex: "-1", title: it.pinned ? "إلغاء التثبيت (Ctrl+P)" : "تثبيت (Ctrl+P)", "aria-label": it.pinned ? "إلغاء التثبيت" : "تثبيت", "aria-pressed": String(it.pinned), onclick: e => { e.stopPropagation(); togglePin(it); } }, icon("pin")),
          h("button", { class: "icon-btn", type: "button", tabindex: "-1", title: "حذف (Delete)", "aria-label": "حذف", onclick: e => { e.stopPropagation(); remove(it); } }, icon("trash"))
        )
      );
      li.addEventListener("mousemove", () => { if (sel !== i) { sel = i; markSelected(false); } });
      li.addEventListener("click", () => { sel = i; markSelected(false); onActivate(it, { plain: false }); });
      return li;
    }

    function markSelected(scroll = true) {
      [...list.children].forEach((li, i) => li.setAttribute("aria-selected", String(i === sel)));
      const cur = list.children[sel];
      if (cur) {
        search.setAttribute("aria-activedescendant", cur.id);
        if (scroll) cur.scrollIntoView({ block: "nearest" });
      } else {
        search.removeAttribute("aria-activedescendant");
      }
    }

    function move(d) {
      if (!items.length) return;
      sel = Math.max(0, Math.min(items.length - 1, sel + d));
      markSelected();
    }

    async function togglePin(it) {
      const r = await lamhaClipboard.setPinned(it.id, !it.pinned);
      if (!r.ok) toast(r.error === "pin_limit" ? "وصلت إلى الحد الأقصى للعناصر المثبتة (٣٠٠)" : "تعذّر التثبيت");
      else toast(it.pinned ? "أُلغي التثبيت" : "تم التثبيت 📌");
      reload();
    }

    async function remove(it) {
      const r = await lamhaClipboard.remove(it.id);
      if (!r.ok) return;
      toast("حُذف العنصر", { label: "تراجع", run: () => lamhaClipboard.restore(it.id).then(reload) });
      reload();
    }

    /** A short message at the bottom; with an action (undo) it stays 5 s. */
    function toast(text, action) {
      const t = h("div", { class: "lc-toast", role: "status" }, h("span", null, text),
        action && h("button", { type: "button", class: "lc-undo", onclick: () => { t.remove(); action.run(); search.focus(); } }, action.label));
      toastBox.replaceChildren(t);
      setTimeout(() => t.remove(), action ? UNDO_MS : 1600);
    }

    function onKey(e) {
      const inSearch = e.target === search;
      if (!inSearch && e.target.closest && e.target.closest("button, input, textarea") && e.key !== "Escape") return;
      const it = items[sel];
      const ctrl = e.ctrlKey && !e.altKey && !e.metaKey;
      if (e.key === "ArrowDown" || e.key === "ArrowUp") { e.preventDefault(); move(e.key === "ArrowDown" ? 1 : -1); }
      else if (e.key === "PageDown" || e.key === "PageUp") { e.preventDefault(); move(e.key === "PageDown" ? 6 : -6); }
      else if (e.key === "Enter" && !ctrl) { if (it) { e.preventDefault(); onActivate(it, { plain: e.shiftKey }); } }
      else if (ctrl && (e.code === "KeyP" || e.key === "p" || e.key === "P")) { e.preventDefault(); if (it) togglePin(it); }
      else if (ctrl && /^Digit[1-9]$/.test(e.code) && mode === "panel") {
        e.preventDefault();
        const n = Number(e.code.slice(5)) - 1;
        if (items[n]) { sel = n; markSelected(); onActivate(items[n], { plain: e.shiftKey }); }
      }
      else if (e.key === "Delete" && it && (!inSearch || search.selectionStart === search.value.length)) { e.preventDefault(); remove(it); }
      else if (onActions && it && ((e.key === "Tab" && !e.shiftKey) || (e.key === "ArrowRight" && !search.value))) { e.preventDefault(); onActions(it); }
      else if (e.key === "Escape") {
        e.preventDefault();
        if (search.value && mode === "tab") { search.value = ""; query = ""; reload(); } else if (onEscape) onEscape();
      }
    }

    lamhaClipboard.onChanged(() => { if (root.isConnected && !root.closest("[hidden]")) reload(); });

    return {
      reload,
      toast,
      focus: () => search.focus(),
      /** Fresh state for a new opening of the panel. */
      reset() { search.value = ""; query = ""; shown = PAGE; sel = 0; setFilter("all"); },
      get selected() { return items[sel] || null; }
    };
  }

  /** Shown instead of the list while clipboard history is off (the default). */
  function optIn(onEnabled) {
    const btn = h("button", {
      class: "btn", type: "button",
      onclick: async () => {
        btn.disabled = true;
        const r = await lamhaClipboard.setEnabled(true);
        btn.disabled = false;
        if (r.ok) onEnabled();
      }
    }, "تفعيل سجل الحافظة");
    const card = h("div", { class: "lc-optin" },
      h("div", { class: "lc-optin-icon", "aria-hidden": "true" }, "📋"),
      h("h2", null, "سجل الحافظة"),
      h("p", null, "يحفظ لمحة ما تنسخه على جهازك فقط، مشفّرًا، لتجده وتلصقه لاحقًا. لا يُرسل أي شيء إلا عند ضغطك على أداة."),
      btn
    );
    card.focusButton = () => btn.focus();
    return card;
  }

  /**
   * A small modal question with Arabic buttons (confirm() would show OK / Cancel).
   * choices: [{ label, value, danger? }]; resolves to the chosen value, or null for Esc / closing.
   */
  function ask(title, text, choices) {
    return new Promise(resolve => {
      const dlg = h("dialog", { class: "lc-dialog", "aria-labelledby": "lcDlgT" },
        h("h2", { id: "lcDlgT" }, title),
        text && h("p", null, text),
        h("div", { class: "lc-dialog-acts" }, choices.map((c, i) => h("button", {
          type: "button", class: "btn small" + (c.danger ? " lc-danger" : i ? " ghost" : ""),
          onclick: () => { dlg.close(); resolve(c.value); }
        }, c.label))));
      dlg.addEventListener("cancel", () => resolve(null));
      dlg.addEventListener("close", () => { dlg.remove(); resolve(null); });
      document.body.append(dlg);
      dlg.showModal();
    });
  }

  return { create, optIn, ask, h, icon, relTime, arNum, appName, highlight };
})();
