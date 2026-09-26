/* Lamha desktop — Lamha's tools on a clip (Phase 4): the actions that fit the clip, then the result as a preview.
 * Quick panel: لصق (Enter) / نسخ / إعادة. Tab: نسخ / إعادة. An AI result is never pasted without this preview.
 * Needs LamhaClipList (h), LamhaAI (shared/lamha-ai.js: error messages, diff, mistake categories) and lamhaClipboard. */
"use strict";
// eslint-disable-next-line no-unused-vars
var LamhaClipActions = (() => {
  const { h } = LamhaClipList;
  const REVIEW_WORD = /^[A-Za-z][A-Za-z'-]{1,40}$/;
  const AI = new Set(["english", "proofread", "summary"]);

  /** The actions that fit a clip (by its language and length); "plain" only where there is an app to paste into. */
  function available(clip, mode) {
    const t = clip.text.trim();
    const words = t.split(/\s+/).filter(Boolean).length;
    const lang = clip.lang;
    const length = clip.length != null ? clip.length : clip.text.length;
    return [
      ["translate", "ترجم", ["en", "mixed", "other"].includes(lang)],
      ["english", "اكتبه بالإنجليزية", ["ar", "mixed"].includes(lang)],
      ["proofread", "دقّق لغويًا", lang === "en"],
      ["summary", "لخّص", length > 400],
      ["lookup", "ابحث", lang === "en" && words >= 1 && words <= 4],
      ["review", "أضف للمراجعة 🔖", REVIEW_WORD.test(t)],
      ["plain", "الصق كنص عادي", mode === "panel"]
    ].filter(a => a[2]).map(([id, label]) => ({ id, label }));
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
        title: code === "offline_mode" ? "ترجمة الجمل تحتاج إلى الإنترنت" : "الكلمة غير موجودة في القاموس المحلي",
        text: "أنت في وضع «القاموس المحلي فقط». غيّر مصدر القاموس من الإعدادات للبحث عبر الإنترنت.",
        settings: ""
      };
    }
    if (code === "rate_limited") return { title: "خدمة الترجمة مشغولة مؤقتًا", text: "أُرسلت طلبات كثيرة في وقت قصير. انتظر دقيقة ثم أعد المحاولة." };
    if (code === "no_translation" || code === "empty_result") return { title: "لا توجد ترجمة لهذا النص", text: "جرّب البحث عنه بدل ذلك." };
    return { title: "تعذّرت الترجمة", text: "حدث خطأ أثناء الاتصال بخدمة الترجمة. تحقق من اتصالك ثم حاول مجددًا." };
  }

  const openSettings = hash => browser.tabs.create({ url: browser.runtime.getURL("options/options.html" + hash) });

  /**
   * Renders the actions for `clip` (a full clip from lamhaClipboard.get) into `root`.
   * @param {object} o
   * @param {"panel"|"tab"} o.mode
   * @param {() => void} [o.onBack]   panel: back to the list (Esc)
   */
  function mount(root, clip, { mode, onBack }) {
    const acts = available(clip, mode);
    let token = 0;
    const items = acts.map(a => h("button", { class: "ca-item", type: "button", role: "menuitem", "data-act": a.id, onclick: () => run(a.id) }, a.label));
    const menu = h("div", { class: "ca-menu", role: "menu", "aria-label": "أدوات لمحة", "aria-orientation": mode === "panel" ? "vertical" : "horizontal" }, items);
    const out = h("div", { class: "ca-out", "aria-live": "polite" });
    const head = mode === "panel" && h("div", { class: "ca-head" },
      h("button", { class: "link", type: "button", onclick: () => onBack && onBack() }, "→ رجوع"),
      h("div", { class: "ca-clip", dir: "auto" }, clip.label || clip.text.trim().slice(0, 200)));
    root.classList.add("ca", "ca-" + mode);
    root.replaceChildren(...[head, menu, out].filter(Boolean));

    root.addEventListener("keydown", e => {
      const i = items.indexOf(document.activeElement);
      const next = { ArrowDown: 1, ArrowUp: -1, ArrowLeft: 1, ArrowRight: -1 }[e.key]; // RTL: ← is "next"
      if (i >= 0 && next && (mode === "panel" ? /Up|Down/.test(e.key) : true)) {
        e.preventDefault();
        items[(i + next + items.length) % items.length].focus();
      } else if (e.key === "Escape" && mode === "panel") {
        e.preventDefault();
        e.stopPropagation(); // the panel's own Esc would close it
        if (out.childElementCount) { out.replaceChildren(); items[0] && items[0].focus(); } else if (onBack) onBack();
      }
    });

    async function run(action, fresh = false, lang) {
      if (action === "plain") return lamhaClipboard.paste(clip.id, true);
      if (action === "lookup") return lamhaClipboard.lookup(clip.id);
      if (action === "summary" && !lang) lang = (await browser.storage.local.get({ clipboardSummaryLang: "ar" })).clipboardSummaryLang;
      const my = ++token;
      items.forEach(b => b.setAttribute("aria-pressed", String(b.dataset.act === action)));
      out.replaceChildren(h("div", { class: "ca-loading", role: "status" }, h("span", { class: "ca-spin", "aria-hidden": "true" }), "جارٍ العمل…"));
      const r = await lamhaClipboard.action(clip.id, action, fresh, lang);
      if (my !== token) return; // another action was chosen meanwhile
      if (!r.ok) return showError(action, r.error);
      show(action, r.data);
    }

    async function showError(action, code) {
      const e = await errorFor(action, code);
      out.replaceChildren(h("div", { class: "ca-error", role: "alert" }, h("b", null, e.title), h("div", null, e.text),
        h("div", { class: "ca-acts" }, e.settings != null
          ? h("button", { class: "btn small", type: "button", onclick: () => openSettings(e.settings) }, e.settings === "#ai" ? "الإعدادات ← أدوات الكتابة" : "الإعدادات")
          : h("button", { class: "btn small", type: "button", onclick: () => run(action) }, "إعادة المحاولة"))));
    }

    function show(action, data) {
      if (action === "review") {
        out.replaceChildren(h("div", { class: "ca-ok", role: "status" },
          data.inDeck ? `أُضيفت «${data.word}» إلى المراجعة 🔖` : `أُزيلت «${data.word}» من المراجعة`,
          data.tr && h("div", { class: "ca-muted", dir: "auto" }, data.tr)));
        return;
      }
      let result, body;
      if (action === "proofread") {
        result = data.corrected.trim();
        if (!data.issues.length || result === clip.text.trim()) {
          out.replaceChildren(h("div", { class: "ca-ok", role: "status" }, "✓ لا توجد أخطاء — نصّك سليم"),
            h("div", { class: "ca-acts" }, h("button", { class: "btn small ghost", type: "button", onclick: () => run(action, true) }, "إعادة")));
          return;
        }
        body = [
          h("div", { class: "ca-text", dir: "ltr" }, LamhaAI.diffNodes(h, clip.text.trim(), result)),
          h("ul", { class: "ca-issues" }, data.issues.map(i => h("li", null,
            h("div", { class: "ca-fix", dir: "ltr" }, h("del", null, i.original), " → ", h("ins", null, i.fix)),
            i.category !== "other" && LamhaAI.CATEGORIES[i.category] && h("span", { class: "ca-cat" }, LamhaAI.CATEGORIES[i.category].ar),
            i.why && h("div", { class: "ca-why" }, i.why))))
        ];
      } else {
        result = data.text;
        body = [h("div", { class: "ca-text", dir: "auto" }, result)];
      }
      if (action === "summary") body.unshift(summaryLangSwitch(data.lang));
      const paste = mode === "panel" && h("button", { class: "btn small", type: "button", onclick: () => lamhaClipboard.pasteResult(clip.id, result) }, "لصق");
      const copy = h("button", {
        class: "btn small" + (paste ? " ghost" : ""), type: "button",
        onclick: async () => { const r = await lamhaClipboard.copyText(result); copy.textContent = r.ok ? "نُسخ ✓" : "تعذّر النسخ"; }
      }, "نسخ");
      out.replaceChildren(...[
        ...body,
        h("div", { class: "ca-acts" }, paste, copy,
          h("button", { class: "btn small ghost", type: "button", title: "نتيجة جديدة", onclick: () => run(action, true, data.lang) }, "إعادة")),
        data.cached && h("div", { class: "ca-muted" }, "نتيجة محفوظة من قبل — «إعادة» تطلب نتيجة جديدة")
      ].filter(Boolean));
      (paste || copy).focus(); // Enter pastes (panel)
    }

    /** العربية | English above a summary: switches (each language is cached) and is remembered for next time. */
    function summaryLangSwitch(current) {
      const pick = lang => {
        browser.storage.local.set({ clipboardSummaryLang: lang });
        if (lang !== current) run("summary", false, lang);
      };
      return h("div", { class: "ca-lang", role: "group", "aria-label": "لغة الملخص" },
        h("span", null, "لغة الملخص:"),
        [["ar", "العربية"], ["en", "English"]].map(([lang, label]) =>
          h("button", { type: "button", class: "lc-chip", lang, "aria-pressed": String(lang === current), onclick: () => pick(lang) }, label)));
    }

    return { focus: () => (items[0] ? items[0].focus() : null), run };
  }

  return { available, mount };
})();
