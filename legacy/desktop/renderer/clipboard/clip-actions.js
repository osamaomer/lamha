/* Lamha desktop — Lamha's tools on a clip (Phase 4): the actions that fit the clip, then the result as a preview.
 * Quick panel: لصق (Enter) / نسخ / إعادة. Tab: نسخ / إعادة. An AI result is never pasted without this preview.
 * Needs LamhaClipList (h), LamhaAI (shared/lamha-ai.js: error messages, diff, mistake categories) and lamhaClipboard. */
"use strict";
// eslint-disable-next-line no-unused-vars
var LamhaClipActions = (() => {
  const { h } = LamhaClipList;
  const L = (key, vars) => LamhaI18n.t(key, vars);
  const REVIEW_WORD = /^[A-Za-z][A-Za-z'-]{1,40}$/;
  const AI = new Set(["english", "proofread", "summary"]);

  /** The actions that fit a clip (by its language and length); "plain" only where there is an app to paste into. */
  function available(clip, mode) {
    const t = clip.text.trim();
    const words = t.split(/\s+/).filter(Boolean).length;
    const lang = clip.lang;
    const length = clip.length != null ? clip.length : clip.text.length;
    return [
      ["translate", "d.aTranslate", ["en", "mixed", "other"].includes(lang)],
      ["english", "d.aEnglish", ["ar", "mixed"].includes(lang)],
      ["proofread", "d.aProofread", lang === "en"],
      ["summary", "d.aSummary", length > 400],
      ["lookup", "d.aLookup", lang === "en" && words >= 1 && words <= 4],
      ["review", "d.aReview", REVIEW_WORD.test(t)],
      ["plain", "d.aPlain", mode === "panel"]
    ].filter(a => a[2]).map(([id, key]) => ({ id, label: L(key) }));
  }

  /** Arabic title, explanation and whether the fix is in Settings — the same wording as the rest of Lamha. */
  async function errorFor(action, code) {
    if (AI.has(action)) {
      const local = await browser.storage.local.get(LamhaAI.PROVIDER_KEYS);
      const [title, text, settings] = LamhaAI.errorInfo(code, LamhaAI.provider(local).name);
      return { title, text, settings: settings ? "#ai" : null };
    }
    if (code === "not_found_offline" || code === "offline_mode") {
      return {
        title: code === "offline_mode" ? L("c.errOfflineMode") : L("c.errNotFound"),
        text: L("c.errLocalOnly"),
        settings: ""
      };
    }
    if (code === "rate_limited") return { title: L("c.errBusy"), text: L("c.errTooMany") };
    if (code === "too_long") { const [title, text] = LamhaAI.errorInfo("ai_too_long"); return { title, text }; }
    if (code === "no_translation" || code === "empty_result") return { title: L("d.errNoTranslation"), text: L("d.errNoTranslationHint") };
    return { title: L("c.errFailed"), text: L("d.errTranslate") };
  }

  const openSettings = hash => browser.tabs.create({ url: browser.runtime.getURL("options/options.html" + hash) });

  /** What each tool does, in a few words (quick panel). */
  const HINTS = { english: "d.hEnglish", proofread: "d.hProofread", summary: "d.hSummary", lookup: "d.hLookup", review: "d.hReview", plain: "d.hPlain" };
  const LATIN_DIGITS = "123456789", ARABIC_DIGITS = "١٢٣٤٥٦٧٨٩";
  function langName(code) {
    try { return new Intl.DisplayNames([LamhaI18n.lang()], { type: "language" }).of(code) || code; } catch (_) { return code; }
  }
  function hintFor(id) {
    const el = h("span", { class: "ca-hint" }, id === "translate" ? "" : L(HINTS[id]));
    if (id === "translate") browser.storage.sync.get({ targetLang: "ar" }).then(s => { el.textContent = L("d.hTranslate", { lang: langName(s.targetLang) }); }, () => {});
    return el;
  }
  const keyHint = (k, label) => h("span", null, h("kbd", null, k), " ", label);

  /**
   * Renders the actions for `clip` (a full clip from lamhaClipboard.get) into `root`.
   * @param {object} o
   * @param {"panel"|"tab"} o.mode
   * @param {() => void} [o.onBack]   panel: back to the list (Esc)
   */
  function mount(root, clip, { mode, onBack }) {
    const panel = mode === "panel";
    const acts = available(clip, mode);
    let token = 0, last = null;
    // quick panel: number, icon, name and what it does; the mouse moves the keyboard's choice, so one row is lit at a time
    const items = acts.map((a, i) => h("button", {
      class: "ca-item", type: "button", role: "menuitem", "data-act": a.id, onclick: () => run(a.id),
      "aria-keyshortcuts": panel && i < 9 ? String(i + 1) : null,
      onmouseenter: panel ? e => e.currentTarget.focus({ preventScroll: true }) : null
    },
      panel && i < 9 && h("span", { class: "ca-num", "aria-hidden": "true" }, LamhaI18n.num(i + 1)),
      h("span", { class: "ca-ico", "aria-hidden": "true" }, LamhaClipList.icon(a.id)),
      h("span", { class: "ca-label" }, a.label),
      panel && hintFor(a.id)));
    const menu = h("div", { class: "ca-menu", role: "menu", "aria-label": L("d.toolsLabel"), "aria-orientation": panel ? "vertical" : "horizontal" }, items);
    const out = h("div", { class: "ca-out", "aria-live": "polite" });
    const lang = { ar: L("lang.ar"), en: L("lang.en") }[clip.lang];
    const head = panel && h("div", { class: "ca-head" },
      h("div", { class: "ca-title" },
        h("button", { class: "icon-btn ca-back", type: "button", title: L("d.backToList"), "aria-label": L("d.backToList"), onclick: () => onBack && onBack() }, LamhaClipList.icon("back")),
        h("strong", null, L("d.toolsTitle"))),
      h("div", { class: "ca-clip" },
        h("div", { class: "ca-clip-text", dir: "auto" }, clip.label || clip.text.trim().slice(0, 400)),
        h("div", { class: "ca-clip-meta" }, [LamhaClipList.appName(clip.sourceApp), LamhaClipList.relTime(clip.lastCopiedAt || clip.createdAt), lang].filter(Boolean).join(" · "))));
    const keys = panel && h("div", { class: "ca-keys", "aria-hidden": "true" });
    root.classList.add("ca", "ca-" + mode);
    root.replaceChildren(...[head, menu, out, keys].filter(Boolean));

    /** "menu" | "result" (a preview: Enter pastes) | "busy" — what shows, and the key hints for it (quick panel). */
    function state(s) {
      if (!panel) return;
      menu.hidden = s !== "menu"; // an answer takes the tools' place
      if (s === "menu") out.replaceChildren();
      keys.replaceChildren(...(s === "menu"
        ? [keyHint("Enter", L("d.kRun")), keyHint(`${LamhaI18n.num(1)}–${LamhaI18n.num(Math.min(9, items.length))}`, L("d.kShortcut")), keyHint("Esc", L("d.kBack"))]
        : s === "result" ? [keyHint("Enter", L("d.kPaste")), keyHint("Esc", L("d.kToolsBack"))] : [keyHint("Esc", L("d.kToolsBack"))]));
    }
    state("menu");

    root.addEventListener("keydown", e => {
      if (panel && !menu.hidden && e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) { // 1…9 (or ١…٩) runs that tool
        const n = LATIN_DIGITS.includes(e.key) ? LATIN_DIGITS.indexOf(e.key) : ARABIC_DIGITS.indexOf(e.key);
        if (n >= 0 && n < items.length) { e.preventDefault(); items[n].focus(); items[n].click(); return; }
      }
      const i = items.indexOf(document.activeElement);
      const next = { ArrowDown: 1, ArrowUp: -1, ArrowLeft: 1, ArrowRight: -1 }[e.key]; // RTL: ← is "next"
      if (i >= 0 && next && (panel ? /Up|Down/.test(e.key) : true)) {
        e.preventDefault();
        items[(i + next + items.length) % items.length].focus();
      } else if (e.key === "Escape" && panel) {
        e.preventDefault();
        e.stopPropagation(); // the panel's own Esc would close it
        if (menu.hidden) { ++token; state("menu"); (items.find(b => b.dataset.act === last) || items[0]).focus(); } // an answer → the tools
        else if (onBack) onBack(); // the tools → the list
      }
    });

    async function run(action, fresh = false, lang) {
      if (action === "plain") return lamhaClipboard.paste(clip.id, true);
      if (action === "lookup") return lamhaClipboard.lookup(clip.id);
      if (action === "summary" && !lang) lang = (await browser.storage.local.get({ clipboardSummaryLang: LamhaI18n.lang() })).clipboardSummaryLang;
      const my = ++token;
      last = action;
      items.forEach(b => b.setAttribute("aria-pressed", String(b.dataset.act === action)));
      state("busy");
      out.replaceChildren(h("div", { class: "ca-loading", role: "status" }, h("span", { class: "ca-spin", "aria-hidden": "true" }), L("common.working")));
      if (panel) root.focus(); // keys (Esc) keep working while the menu is hidden
      const r = await lamhaClipboard.action(clip.id, action, fresh, lang);
      if (my !== token) return; // another action was chosen meanwhile
      if (!r.ok) return showError(action, r.error);
      show(action, r.data);
    }

    async function showError(action, code) {
      const e = await errorFor(action, code);
      out.replaceChildren(h("div", { class: "ca-error", role: "alert" }, h("b", null, e.title), h("div", null, e.text),
        h("div", { class: "ca-acts" }, e.settings != null
          ? h("button", { class: "btn small", type: "button", onclick: () => openSettings(e.settings) }, e.settings === "#ai" ? L("d.settingsWriting") : L("common.settings"))
          : h("button", { class: "btn small", type: "button", onclick: () => run(action) }, L("common.retry")))));
    }

    function show(action, data) {
      if (action === "review") {
        out.replaceChildren(h("div", { class: "ca-ok", role: "status" },
          data.inDeck ? L("d.addedReview", { w: data.word }) : L("d.removedReview", { w: data.word }),
          data.tr && h("div", { class: "ca-muted", dir: "auto" }, data.tr)));
        return;
      }
      let result, body;
      if (action === "proofread") {
        result = data.corrected.trim();
        if (!data.issues.length || result === clip.text.trim()) {
          out.replaceChildren(h("div", { class: "ca-ok", role: "status" }, L("write.noErrorsCheck")),
            h("div", { class: "ca-acts" }, h("button", { class: "btn small ghost", type: "button", onclick: () => run(action, true) }, L("d.redo"))));
          return;
        }
        body = [
          h("div", { class: "ca-text", dir: "ltr" }, LamhaAI.diffNodes(h, clip.text.trim(), result)),
          h("ul", { class: "ca-issues" }, data.issues.map(i => h("li", null,
            h("div", { class: "ca-fix", dir: "ltr" }, h("del", null, i.original), " → ", h("ins", null, i.fix)),
            i.category !== "other" && LamhaAI.isCategory(i.category) && h("span", { class: "ca-cat" }, LamhaAI.catLabel(i.category)),
            i.why && h("div", { class: "ca-why", dir: "auto" }, i.why))))
        ];
      } else {
        result = data.text;
        body = [h("div", { class: "ca-text", dir: "auto" }, result)];
      }
      if (action === "summary") body.unshift(summaryLangSwitch(data.lang));
      const paste = mode === "panel" && h("button", { class: "btn small", type: "button", onclick: () => lamhaClipboard.pasteResult(clip.id, result) }, L("d.paste"));
      const copy = h("button", {
        class: "btn small" + (paste ? " ghost" : ""), type: "button",
        onclick: async () => { const r = await lamhaClipboard.copyText(result); copy.textContent = r.ok ? L("d.copied") : L("d.copyFailed"); }
      }, L("common.copy"));
      out.replaceChildren(...[
        ...body,
        h("div", { class: "ca-acts" }, paste, copy,
          h("button", { class: "btn small ghost", type: "button", title: L("d.newResult"), onclick: () => run(action, true, data.lang) }, L("d.redo"))),
        data.cached && h("div", { class: "ca-muted" }, L("d.cached"))
      ].filter(Boolean));
      // motion: proofreading marks one after another; other answers word by word (a cached one is already known: no show)
      const text = out.querySelector(".ca-text");
      if (text && action === "proofread") LamhaMotion.sequence(text, "del, ins");
      else if (text && !data.cached) LamhaMotion.typeIn(text);
      state("result");
      (paste || copy).focus(); // Enter pastes (panel)
    }

    /** العربية | English above a summary: switches (each language is cached) and is remembered for next time. */
    function summaryLangSwitch(current) {
      const pick = lang => {
        browser.storage.local.set({ clipboardSummaryLang: lang });
        if (lang !== current) run("summary", false, lang);
      };
      return h("div", { class: "ca-lang", role: "group", "aria-label": L("d.summaryLang") },
        h("span", null, L("d.summaryLangColon")),
        [["ar", "العربية"], ["en", "English"]].map(([lang, label]) =>
          h("button", { type: "button", class: "lc-chip", lang, "aria-pressed": String(lang === current), onclick: () => pick(lang) }, label)));
    }

    if (panel) root.tabIndex = -1;
    return { focus: () => (items[0] ? items[0].focus() : null), run };
  }

  return { available, mount };
})();
