/* Lamha desktop — clipboard history list, shared by the quick panel (panel.js) and the الحافظة tab (tab.js).
 * Search box, الكل / المثبتة chips, rows with highlighted matches, keyboard selection, delete with undo.
 * Talks to the main process through window.lamhaClipboard (preload.js); needs LamhaArabic (shared/arabic-normalize.js). */
"use strict";
// eslint-disable-next-line no-unused-vars
var LamhaClipList = (() => {
  const L = (key, vars) => LamhaI18n.t(key, vars); // interface text: shared/i18n.js + renderer/i18n-desktop.js
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

  /** Numbers as the interface writes them (Arabic-Indic digits in Arabic). */
  const arNum = n => LamhaI18n.num(n);

  /** "قبل ٥ دقائق" / "5 minutes ago" (Arabic counting rules in i18n-desktop.js). */
  function relTime(ts, now = Date.now()) {
    const s = Math.max(0, Math.round((now - ts) / 1000));
    if (s < 60) return L("d.now");
    const m = Math.floor(s / 60);
    if (m < 60) return L("d.minAgo", { n: m });
    const hr = Math.floor(m / 60);
    if (hr < 24) return L("d.hourAgo", { n: hr });
    const d = Math.floor(hr / 24);
    if (d === 1) return L("d.yesterday");
    if (d < 30) return L("d.dayAgo", { n: d });
    return new Date(ts).toLocaleDateString(LamhaI18n.lang() === "ar" ? "ar-EG" : "en-US", { day: "numeric", month: "long", year: "numeric" });
  }

  const appName = app => (app === "lamha" ? L("d.appLamha") : app === "unknown" ? L("d.appUnknown") : app.replace(/\.exe$/i, ""));

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
    let entrance = false, pinnedNow = null; // motion: rows come in one after another on opening; a new pin drops in

    const search = h("input", {
      class: "lc-search", type: "search", dir: "auto", placeholder: L("d.search"), "aria-label": L("d.searchLabel"),
      role: "combobox", "aria-controls": id + "-list", "aria-expanded": "true", "aria-autocomplete": "list", spellcheck: "false"
    });
    const chips = ["all", "pinned"].map(f => h("button", { class: "lc-chip", type: "button", "aria-pressed": String(f === filter), onclick: () => setFilter(f) }, f === "all" ? L("d.all") : L("d.pinnedFilter")));
    const list = h("ul", { class: "lc-list", id: id + "-list", role: "listbox", "aria-label": L("d.historyLabel") });
    const more = h("button", { class: "btn small ghost lc-more", type: "button", hidden: true, onclick: () => { shown += PAGE; reload(); } }, L("d.showMore"));
    const empty = h("p", { class: "lc-empty", hidden: true, role: "status" });
    const toastBox = h("div", { class: "lc-toasts", "aria-live": "polite" });
    root.classList.add("lc", "lc-" + mode);
    root.replaceChildren(
      h("div", { class: "lc-bar" }, search, h("div", { class: "lc-chips", role: "group", "aria-label": L("d.filter") }, chips)),
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
      const rows = items.map((it, i) => row(it, i));
      if (entrance) { list.replaceChildren(...rows); LamhaMotion.stagger(rows, { each: 15, max: 8, duration: 180 }); }
      else LamhaMotion.flip(list, () => list.replaceChildren(...rows)); // rows glide to their new places (search, pin)
      entrance = false;
      if (pinnedNow) {
        const pin = list.querySelector(`[data-id="${CSS.escape(pinnedNow)}"] .lc-pin`);
        if (pin) LamhaMotion.play(pin, [{ opacity: 0, transform: "translateY(-10px) rotate(-25deg) scale(1.3)" }, { opacity: 1, transform: "none" }], { duration: 360, easing: "cubic-bezier(.3,1.5,.5,1)", fullOnly: true });
        pinnedNow = null;
      }
      more.hidden = items.length >= total;
      empty.hidden = items.length > 0;
      empty.textContent = query ? L("d.noResults") : filter === "pinned" ? L("d.noPinned") : L("d.empty");
      markSelected();
    }

    function row(it, i) {
      const meta = h("div", { class: "lc-meta" },
        mode === "panel" && i < 9 && h("kbd", { "aria-hidden": "true" }, String(i + 1)),
        h("span", { class: "lc-app", dir: "auto" }, appName(it.sourceApp)),
        h("span", { "aria-hidden": "true" }, "·"),
        h("span", null, relTime(Math.max(it.lastCopiedAt, it.lastUsedAt))),
        it.pinned && h("span", { class: "lc-pin", title: L("d.pinned"), "aria-label": L("d.pinned") }, "📌")
      );
      const li = h("li", { class: "lc-row", role: "option", id: `${id}-o${i}`, "aria-selected": "false", "data-id": it.id },
        h("div", { class: "lc-main" },
          it.label && h("b", { class: "lc-label", dir: "auto" }, highlight(it.label, query)),
          h("div", { class: "lc-text", dir: "auto" }, highlight(rowText(it.text), query)),
          it.translation && h("div", { class: "lc-tr", dir: "rtl" }, it.translation), // cached translation of an English clip
          meta
        ),
        h("div", { class: "lc-tools" },
          h("button", { class: "icon-btn", type: "button", tabindex: "-1", title: it.pinned ? L("d.unpinKey") : L("d.pinKey"), "aria-label": it.pinned ? L("d.unpin") : L("d.pin"), "aria-pressed": String(it.pinned), onclick: e => { e.stopPropagation(); togglePin(it); } }, icon("pin")),
          h("button", { class: "icon-btn", type: "button", tabindex: "-1", title: L("d.deleteKey"), "aria-label": L("d.delete"), onclick: e => { e.stopPropagation(); remove(it); } }, icon("trash"))
        )
      );
      li.addEventListener("mousemove", () => { if (sel !== i) { sel = i; markSelected(false); } });
      li.addEventListener("click", () => { sel = i; markSelected(false); activate(it, { plain: false }); });
      return li;
    }

    /** Panel: the chosen row flashes before the panel closes to paste it (full animations; ~110 ms). */
    async function activate(it, opts) {
      const li = mode === "panel" && list.querySelector(`[data-id="${CSS.escape(it.id)}"]`);
      if (li) await LamhaMotion.exit(li, [{ transform: "none", filter: "none" }, { transform: "scale(.97)", filter: "brightness(1.12)" }], { duration: 110, easing: "ease-out", fullOnly: true });
      onActivate(it, opts);
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
      if (!r.ok) toast(r.error === "pin_limit" ? L("d.pinLimit") : L("d.pinFailed"));
      else { toast(it.pinned ? L("d.unpinned") : L("d.pinnedToast")); if (!it.pinned) pinnedNow = it.id; }
      reload();
    }

    async function remove(it) {
      // the row slides away while the request runs; the rows below then glide up (flip in render)
      const li = list.querySelector(`[data-id="${CSS.escape(it.id)}"]`);
      const end = li && getComputedStyle(li).direction === "rtl" ? -1 : 1;
      const [r] = await Promise.all([
        lamhaClipboard.remove(it.id),
        li ? LamhaMotion.exit(li, [{ opacity: 1, transform: "none" }, { opacity: 0, transform: `translateX(${24 * end}px)` }], { duration: 140 }) : null
      ]);
      if (!r.ok) { if (li) li.getAnimations().forEach(a => a.cancel()); return; }
      toast(L("d.deleted"), { label: L("d.undo"), run: () => lamhaClipboard.restore(it.id).then(reload) });
      reload();
    }

    /** A short message at the bottom; with an action (undo) it stays 5 s, with a bar showing the time left. */
    function toast(text, action) {
      const ms = action ? UNDO_MS : 1600;
      const t = h("div", { class: "lc-toast", role: "status" }, h("span", null, text),
        action && h("button", { type: "button", class: "lc-undo", onclick: () => { t.remove(); action.run(); search.focus(); } }, action.label),
        action && h("span", { class: "flash-bar", "aria-hidden": "true", style: `--undo:${ms}ms` }));
      toastBox.replaceChildren(t);
      setTimeout(() => {
        if (!t.isConnected) return;
        LamhaMotion.exit(t, [{ opacity: 1 }, { opacity: 0, transform: "translateY(6px)" }], { duration: 140 }).then(() => t.remove());
      }, ms);
    }

    function onKey(e) {
      const inSearch = e.target === search;
      if (!inSearch && e.target.closest && e.target.closest("button, input, textarea") && e.key !== "Escape") return;
      const it = items[sel];
      const ctrl = e.ctrlKey && !e.altKey && !e.metaKey;
      if (e.key === "ArrowDown" || e.key === "ArrowUp") { e.preventDefault(); move(e.key === "ArrowDown" ? 1 : -1); }
      else if (e.key === "PageDown" || e.key === "PageUp") { e.preventDefault(); move(e.key === "PageDown" ? 6 : -6); }
      else if (e.key === "Enter" && !ctrl) { if (it) { e.preventDefault(); activate(it, { plain: e.shiftKey }); } }
      else if (ctrl && (e.code === "KeyP" || e.key === "p" || e.key === "P")) { e.preventDefault(); if (it) togglePin(it); }
      else if (ctrl && /^Digit[1-9]$/.test(e.code) && mode === "panel") {
        e.preventDefault();
        const n = Number(e.code.slice(5)) - 1;
        if (items[n]) { sel = n; markSelected(); activate(items[n], { plain: e.shiftKey }); }
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
      reset() { search.value = ""; query = ""; shown = PAGE; sel = 0; entrance = true; setFilter("all"); },
      /** The next redraw brings the rows in one after another instead of gliding them (a fresh opening). */
      enter() { entrance = true; },
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
    }, L("d.enable"));
    const card = h("div", { class: "lc-optin" },
      h("div", { class: "lc-optin-icon", "aria-hidden": "true" }, "📋"),
      h("h2", null, L("d.optInTitle")),
      h("p", null, L("d.optInText")),
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
