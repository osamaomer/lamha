/* Lamha — content script: selection pill, lookup card, page-translation bar. */
/* global LAMHA_CSS, LamhaPage, LamhaAI */
(() => {
  "use strict";
  const L = (key, vars) => LamhaI18n.t(key, vars); // interface text (shared/i18n.js)
  const arrow = () => (LamhaI18n.lang() === "ar" ? "←" : "→");
  if (window.__lamhaLoaded) return;
  window.__lamhaLoaded = true;

  const IS_TOP = window === window.top;
  const MAX_SELECTION = 3000;
  const ARABIC_RE = /[؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]/;
  const LATIN_RE = /[A-Za-z]/;
  const LETTER_RE = /\p{L}/u;

  let settings = {
    enabled: true, targetLang: "ar", triggerMode: "button", reverseForArabic: true,
    showInInputs: false, showWikipedia: true, translateDefinitions: true, autoSpeak: false, theme: "auto",
    dictSource: "local", useContext: true, aiInInputs: true, disabledSites: []
  };
  // writing tools are available once Claude, Gemini (API key) or Ollama (a local model) is set up
  const AI_LOCAL_KEYS = LamhaAI.PROVIDER_KEYS;
  let aiLocal = {}, aiReady = false;
  const aiProvider = () => LamhaAI.provider(aiLocal);
  const aiTranslator = () => LamhaAI.translator(aiLocal); // Settings → Translation service
  const updateAiReady = () => { aiReady = aiProvider().ready; };
  browser.storage.sync.get(settings).then(s => { settings = { ...settings, ...s }; applyTheme(); }).catch(() => {});
  // interface language: the card's labels follow it; an open card closes so the next one is in the new language
  LamhaI18n.init({ onChange: () => { if (root) root.classList.toggle("en", LamhaI18n.lang() === "en"); closeCard(); hidePill(); } }).catch(() => {});
  browser.storage.local.get(AI_LOCAL_KEYS).then(r => { aiLocal = r || {}; updateAiReady(); }).catch(() => {});
  browser.storage.onChanged.addListener((changes, area) => {
    if (area === "local") {
      AI_LOCAL_KEYS.forEach(k => { if (changes[k]) aiLocal[k] = changes[k].newValue; });
      updateAiReady();
    }
    if (area !== "sync") return;
    for (const k in changes) settings[k] = changes[k].newValue;
    applyTheme();
    if (!isActiveHere()) closeAll();
  });

  const isActiveHere = () => settings.enabled && !(settings.disabledSites || []).includes(location.hostname);

  /* ---------------- helpers ---------------- */

  const send = msg => browser.runtime.sendMessage(msg).catch(e => ({ ok: false, error: String(e && e.message || e) }));

  function h(tag, props, ...kids) {
    const el = document.createElement(tag);
    if (props) for (const [k, v] of Object.entries(props)) {
      if (v == null || v === false) continue;
      if (k === "class") el.className = v;
      else if (k.startsWith("on") && typeof v === "function") el.addEventListener(k.slice(2), v);
      else if (k === "style" && typeof v === "object") Object.assign(el.style, v);
      else el.setAttribute(k, v === true ? "" : v);
    }
    for (const c of kids.flat(Infinity)) {
      if (c == null || c === false) continue;
      el.append(c.nodeType ? c : String(c));
    }
    return el;
  }

  const ICONS = {
    speak: ["M11 5 6 9H2v6h4l5 4V5z", "M15.54 8.46a5 5 0 0 1 0 7.07", "M19.07 4.93a10 10 0 0 1 0 14.14"],
    copy: ["M20 9h-9a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2z", "M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"],
    close: ["M18 6 6 18", "M6 6l12 12"],
    settings: ["M4 21v-7", "M4 10V3", "M12 21v-9", "M12 8V3", "M20 21v-5", "M20 12V3", "M1 14h6", "M9 8h6", "M17 16h6"],
    back: ["M9 18l6-6-6-6"],
    external: ["M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"],
    search: ["M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z", "M20 20l-3.5-3.5"],
    translate: ["M5 8l6 6", "M4 14l6-6 2-3", "M2 5h12", "M7 2h1", "M22 22l-5-10-5 10", "M14 18h6"],
    check: ["M20 6 9 17l-5-5"],
    retry: ["M3 12a9 9 0 1 0 3-6.7L3 8", "M3 3v5h5"],
    book: ["M4 19.5A2.5 2.5 0 0 1 6.5 17H20", "M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"],
    sparkle: ["M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9L12 3z", "M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8L19 15z"],
    bookmark: ["M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"]
  };
  function icon(name, size = 16, sw = 2) {
    const NS = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(NS, "svg");
    for (const [k, v] of Object.entries({ viewBox: "0 0 24 24", width: size, height: size, fill: "none", stroke: "currentColor", "stroke-width": sw, "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true" })) svg.setAttribute(k, v);
    for (const d of ICONS[name]) { const p = document.createElementNS(NS, "path"); p.setAttribute("d", d); svg.append(p); }
    return svg;
  }

  let langNames = null, langNamesIn = "";
  function langName(code) {
    if (code === "ar" || code === "en") return L("lang." + code);
    const ui = LamhaI18n.lang();
    try {
      if (!langNames || langNamesIn !== ui) { langNames = new Intl.DisplayNames([ui], { type: "language" }); langNamesIn = ui; }
      return langNames.of(code) || code;
    } catch (_) { return code; }
  }
  const dirOf = LamhaI18n.textDir;

  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); toast(L("c.copiedToast")); }
    catch (_) { toast(L("common.copyFailed")); }
  }

  function speak(text, lang, btn) {
    if (!text) return;
    if (btn && btn.classList.contains("playing")) { // second tap stops
      send({ type: "stopSpeak" });
      try { speechSynthesis.cancel(); } catch (_) { /* no system voice */ }
      btn.classList.remove("playing");
      return;
    }
    if (root) root.querySelectorAll(".playing").forEach(b => b.classList.remove("playing"));
    btn && btn.classList.add("playing");
    const stop = () => btn && btn.classList.remove("playing");
    const viaNetwork = settings.dictSource !== "offline" && navigator.onLine;
    (viaNetwork ? send({ type: "speak", text, lang }) : Promise.resolve(null)).then(r => {
      if (r && r.ok) { stop(); return; } // the background answers once playback has finished
      try {
        const u = new SpeechSynthesisUtterance(text);
        u.lang = lang === "ar" ? "ar-SA" : lang === "en" ? "en-US" : lang;
        u.onend = stop; u.onerror = stop;
        speechSynthesis.cancel(); speechSynthesis.speak(u);
      } catch (_) { stop(); }
    });
  }

  /* ---------------- shadow host ---------------- */

  let host, shadow, root;
  function ensureHost() {
    if (host && host.isConnected) return root;
    host = document.createElement("lamha-ui");
    host.setAttribute("style", "all:initial !important;position:fixed !important;top:0 !important;left:0 !important;width:0 !important;height:0 !important;z-index:2147483647 !important;display:block !important;");
    shadow = host.attachShadow({ mode: "closed" });
    const style = document.createElement("style");
    style.textContent = LAMHA_CSS;
    root = h("div", { class: "root" + (LamhaI18n.lang() === "en" ? " en" : "") });
    LamhaMotion.attach(root); // data-motion: the Animations setting (never on the page's own elements)
    shadow.append(style, root);
    (document.documentElement || document.body).appendChild(host);
    applyTheme();
    return root;
  }

  const mq = window.matchMedia("(prefers-color-scheme: dark)");
  function applyTheme() {
    if (!root) return;
    const dark = settings.theme === "dark" || (settings.theme === "auto" && mq.matches);
    root.classList.toggle("dark", dark);
  }
  mq.addEventListener("change", applyTheme);

  const fromUs = e => host && e.composedPath && e.composedPath().includes(host);

  let toastTimer;
  /** `ok`: a done-with-success message (replaced, inserted) gets a check mark. */
  function toast(text, ok = false) {
    ensureHost();
    const old = root.querySelector(".toast"); if (old) old.remove();
    const t = h("div", { class: "toast", role: "status" }, ok && h("span", { class: "ok-i" }, icon("check", 15, 2.6)), text);
    root.append(t);
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.remove(), 1400);
  }

  /* ---------------- selection ---------------- */

  let lastPointer = null;

  const isTextField = el => !!el && (el.tagName === "TEXTAREA" || (el.tagName === "INPUT" && /^(text|search|url|email|)$/i.test(el.type || "")));

  function fieldPoint(el) {
    const r = el.getBoundingClientRect();
    const p = lastPointer || { x: r.left + 20, y: r.top + 10 };
    return { x: p.x, y: Math.max(r.top, Math.min(p.y, r.bottom)) };
  }

  function editableSelection() {
    const ae = document.activeElement;
    if (!isTextField(ae)) return null;
    const { selectionStart: s, selectionEnd: e } = ae;
    if (s == null || s === e) return { field: true, text: "" };
    const text = ae.value.slice(s, e);
    return {
      field: true, text, point: fieldPoint(ae),
      context: sentenceAround(ae.value.slice(Math.max(0, s - 400), s), ae.value.slice(e, e + 400)),
      editable: { kind: "field", el: ae, start: s, end: e, original: text }
    };
  }

  /** The outermost contenteditable element (the rich-text editor) containing `el`. */
  function editingHost(el) {
    let host = el;
    while (host.parentElement && host.parentElement.isContentEditable) host = host.parentElement;
    return host;
  }

  /** Nothing selected but the caret is in a text box: the writing tools work on all of its text. */
  function wholeEditable() {
    const ae = document.activeElement;
    if (isTextField(ae) && ae.value.trim()) {
      return { text: ae.value, raw: ae.value, point: fieldPoint(ae), editable: { kind: "field", el: ae, start: 0, end: ae.value.length, original: ae.value } };
    }
    if (ae && ae.isContentEditable) {
      const host = editingHost(ae);
      if (!host.innerText.trim()) return null;
      const range = document.createRange();
      range.selectNodeContents(host);
      return { text: host.innerText, raw: host.innerText, range, editable: { kind: "rich", el: host, range, original: range.toString() } };
    }
    return null;
  }

  /* ---- the sentence around a selection: lets the lookup pick the meaning that fits ---- */
  const BLOCK_SEL = "p,li,td,th,dd,dt,blockquote,figcaption,caption,summary,h1,h2,h3,h4,h5,h6,article,section,div,body";

  /** Reads at most ~400 characters on each side of the selection, walking text nodes of its block. */
  function contextOf(range) {
    try {
      // selections can start/end on an element boundary: move to the nearest text position
      const textPos = (node, offset, atStart) => {
        if (node.nodeType === 3) return [node, offset];
        const w = document.createTreeWalker(node, NodeFilter.SHOW_TEXT);
        const child = node.childNodes[atStart ? offset : offset - 1];
        if (child) {
          if (child.nodeType === 3) return [child, atStart ? 0 : child.nodeValue.length];
          w.currentNode = child;
          const t = atStart ? w.nextNode() : (() => { let last = null, n; while ((n = w.nextNode()) && child.contains(n)) last = n; return last; })();
          if (t) return [t, atStart ? 0 : t.nodeValue.length];
        }
        return [null, 0];
      };
      const [sc, so] = textPos(range.startContainer, range.startOffset, true);
      const [ec, eo] = textPos(range.endContainer, range.endOffset, false);
      if (!sc || !ec) return null;
      const block = (sc.parentElement && sc.parentElement.closest(BLOCK_SEL)) || document.body;
      const w = document.createTreeWalker(block, NodeFilter.SHOW_TEXT, {
        acceptNode: n => (/^(SCRIPT|STYLE|NOSCRIPT|TEXTAREA)$/.test(n.parentElement && n.parentElement.tagName) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT)
      });
      let before = sc.nodeValue.slice(0, so);
      w.currentNode = sc;
      for (let n; before.length < 400 && (n = w.previousNode());) before = n.nodeValue + before;
      let after = ec.nodeValue.slice(eo);
      w.currentNode = ec;
      for (let n; after.length < 400 && (n = w.nextNode());) after += n.nodeValue;
      return sentenceAround(before.slice(-400), after.slice(0, 400));
    } catch (_) { return null; }
  }

  function sentenceAround(before, after) {
    let b = (before || "").replace(/\s+/g, " "), a = (after || "").replace(/\s+/g, " ");
    const cut = Math.max(b.lastIndexOf(". "), b.lastIndexOf("! "), b.lastIndexOf("? "), b.lastIndexOf("؟ "));
    if (cut >= 0) b = b.slice(cut + 2);
    else if (b.length > 250) b = b.slice(-250).replace(/^\S*\s/, "");
    const m = a.match(/[.!?؟](\s|$)/);
    if (m) a = a.slice(0, m.index + 1);
    else if (a.length > 250) a = a.slice(0, 250).replace(/\s\S*$/, "");
    const words = (b + " " + a).trim().split(/\s+/).filter(w => LETTER_RE.test(w)).length;
    return words >= 2 ? { before: b, after: a } : null;
  }

  /* `aiOnly`: the selection is shown only with the writing tools (text fields when "work inside
   * text fields" is off, text too long to translate, Arabic text when Arabic→English is off). */
  function getSelectionInfo(force = false) {
    const field = editableSelection();
    const aiInFields = aiReady && settings.aiInInputs;
    let info;
    if (field) {
      if (!settings.showInInputs && !aiInFields && !force) return null;
      info = { text: field.text, point: field.point, context: field.context, editable: field.editable, aiOnly: !settings.showInInputs && !force };
    } else {
      const sel = window.getSelection();
      if (!sel || sel.isCollapsed || !sel.rangeCount) return null;
      const range = sel.getRangeAt(0);
      const anc = range.commonAncestorContainer;
      const el = anc.nodeType === 1 ? anc : anc.parentElement;
      const rich = !!(el && el.isContentEditable);
      if (!force && !settings.showInInputs && !aiInFields && rich) return null;
      info = { text: sel.toString(), range: range.cloneRange(), point: lastPointer };
      if (rich) {
        info.editable = { kind: "rich", el: editingHost(el), range: range.cloneRange(), original: range.toString() };
        info.aiOnly = !settings.showInInputs && !force;
      }
    }
    info.raw = info.text || ""; // line breaks kept for the writing tools
    info.text = info.raw.replace(/\s+/g, " ").trim();
    if (!info.text || !LETTER_RE.test(info.text)) return null;
    if (info.text.length > MAX_SELECTION) {
      if (!aiReady) return null;
      info.aiOnly = true;
    }
    if (!isWordish(info.text)) info.context = null; // for ranges, computed lazily when the card opens
    if (!force && settings.targetLang === "ar" && ARABIC_RE.test(info.text) && !LATIN_RE.test(info.text) && !settings.reverseForArabic) {
      if (!aiReady) return null;
      info.aiOnly = true;
    }
    return info;
  }

  const isWordish = t => t.split(" ").length <= 3 && t.length <= 40 && !/[.!?;:]\s/.test(t);

  function anchorRects(info) {
    if (info.range) {
      const rects = Array.from(info.range.getClientRects()).filter(r => r.width || r.height);
      const b = info.range.getBoundingClientRect();
      if (rects.length) return { box: b, last: rects[rects.length - 1], first: rects[0] };
    }
    const p = info.point || { x: innerWidth / 2, y: 80 };
    const r = new DOMRect(p.x, p.y - 10, 1, 20);
    return { box: r, last: r, first: r };
  }

  /* ---------------- pill ---------------- */

  let pill = null, pillInfo = null;

  function showPill(info) {
    ensureHost();
    hidePill();
    pillInfo = info;
    const word = isWordish(info.text) && !ARABIC_RE.test(info.text);
    const showAI = aiReady && (info.aiOnly || !!info.editable || !word);
    const open = fn => e => { e.preventDefault(); e.stopPropagation(); const i = pillInfo; hidePill(); fn(i, { focus: e.detail === 0 }); };
    pill = h("div", {
      class: "pill", role: "toolbar", "aria-label": L("c.pill"),
      onpointerdown: e => { e.preventDefault(); e.stopPropagation(); } // keep the page selection
    },
      !info.aiOnly && h("button", {
        class: "pill-btn", type: "button", "aria-label": word ? L("c.lookupWord") : L("c.translateText"), title: "Alt+Shift+L", onclick: open(openCard)
      }, icon(word ? "search" : "translate", 15, 2.2), word ? L("c.lookupBtn") : L("c.translateBtn")),
      !info.aiOnly && showAI && h("span", { class: "pill-sep" }),
      showAI && h("button", {
        class: "pill-btn", type: "button", "aria-label": L("p.writeTools"), title: "Alt+Shift+W", onclick: open(openWriteCard)
      }, icon("sparkle", 15, 2), L("c.writeBtn"))
    );
    root.append(pill);
    placePill();
  }

  function placePill() {
    if (!pill || !pillInfo) return;
    const { last, first } = anchorRects(pillInfo);
    const w = pill.offsetWidth || 80, hgt = 32;
    const px = lastPointer ? Math.min(Math.max(lastPointer.x, last.left), last.right) : last.left + last.width / 2;
    let x = Math.round(px - w / 2);
    x = Math.max(8, Math.min(x, innerWidth - w - 8));
    let y = last.top - hgt - 10;
    let below = false;
    if (y < 8) { y = last.bottom + 10; below = true; }
    if (y + hgt > innerHeight - 8) y = Math.max(8, first.top - hgt - 10), below = false;
    pill.classList.toggle("below", below);
    pill.style.left = x + "px";
    pill.style.top = y + "px";
    pill.style.setProperty("--arrow-x", Math.max(14, Math.min(w - 14, px - x)) + "px");
  }

  function hidePill() {
    if (pill) pill.remove();
    pill = null; pillInfo = null;
  }

  /* ---------------- card ---------------- */

  let card = null, cardInfo = null, bodyEl = null, side = "below";
  let stack = [], reqId = 0;

  function openCard(info, { focus = false } = {}) {
    if (!info) return;
    ensureHost();
    hidePill();
    closeCard({ restore: false, animate: false });
    if (focus) returnFocus = document.activeElement;
    if (info.context === undefined) info.context = info.range && isWordish(info.text) ? contextOf(info.range) : null;
    cardInfo = info;
    const q = isWordish(info.text) ? info.text : (info.raw || info.text).trim(); // sentences keep their line breaks
    stack = [q];
    card = h("div", { class: "card", role: "dialog", "aria-label": L("c.cardLabel"), tabindex: "-1" });
    root.append(card);
    decideSide();
    load(q, info.context);
    placeCard();
    if (focus) card.focus({ preventScroll: true });
  }

  function decideSide() {
    const { box } = anchorRects(cardInfo);
    const below = innerHeight - box.bottom - 18;
    const above = box.top - 18;
    side = below >= 320 || below >= above ? "below" : "above";
    card.classList.toggle("above", side === "above");
  }

  function placeCard() {
    if (!card || !cardInfo) return;
    const { box } = anchorRects(cardInfo);
    const w = Math.min(card.classList.contains("write") ? 440 : 384, innerWidth - 16);
    const multiLine = box.height > 40;
    const cx = multiLine && lastPointer ? lastPointer.x : box.left + box.width / 2;
    const left = Math.max(8, Math.min(Math.round(cx - w / 2), innerWidth - w - 8));
    card.style.left = left + "px";
    // it grows out of (and, when sent off, back into) the point it was asked for
    const px = cardInfo.point ? cardInfo.point.x : lastPointer ? lastPointer.x : cx;
    card.style.transformOrigin = `${Math.round(Math.max(0, Math.min(w, px - left)))}px ${side === "below" ? "0" : "100%"}`;
    if (side === "below") {
      const top = Math.max(8, Math.min(box.bottom + 10, innerHeight - 180));
      card.style.top = top + "px"; card.style.bottom = "auto";
      card.style.maxHeight = Math.min(540, innerHeight - top - 8) + "px";
    } else {
      const bottom = Math.max(8, Math.min(innerHeight - box.top + 10, innerHeight - 180));
      card.style.bottom = bottom + "px"; card.style.top = "auto";
      card.style.maxHeight = Math.min(540, innerHeight - bottom - 8) + "px";
    }
  }

  let returnFocus = null; // where the keyboard was before a card opened from the keyboard
  /** `restore`: give the focus back to the page if it was in the card (Esc, ✕); not when another card replaces it
   *  or a click elsewhere closed it. `animate`: a short exit (not when another card takes its place); `send`: the
   *  card shrinks back into its point (Replace / Insert). Returns a promise that settles once the card is gone. */
  function closeCard({ restore = true, animate = true, send = false } = {}) {
    reqId++;
    const old = card;
    const back = returnFocus;
    const focusedInCard = !!old && shadow && !!shadow.activeElement;
    if (restore) returnFocus = null;
    if (restore && focusedInCard && back && back.isConnected) { try { back.focus({ preventScroll: true }); } catch (_) { /* not focusable any more */ } }
    card = null; cardInfo = null; bodyEl = null; stack = [];
    if (!old) return Promise.resolve();
    let gone = Promise.resolve();
    if (animate && LamhaMotion.any()) {
      old.style.pointerEvents = "none";
      old.setAttribute("aria-hidden", "true");
      const up = old.classList.contains("above");
      gone = LamhaMotion.exit(old, send
        ? [{ opacity: 1, transform: "none" }, { opacity: 0, transform: "scale(.55)" }]
        : [{ opacity: 1, transform: "none" }, { opacity: 0, transform: `translateY(${up ? 4 : -4}px) scale(.97)` }],
      { duration: send ? 150 : 110 });
    }
    gone = gone.then(() => old.remove());
    // desktop app: tell it when the card is really gone (not replaced by another one) so it hides its window
    if (window.lamhaDesktop) gone.then(() => { if (!card) window.lamhaDesktop.closed(); });
    return gone;
  }
  function closeAll() { hidePill(); closeCard(); }

  /* ----- card chrome ----- */

  function frame(data, title) {
    card.textContent = "";
    const langLabel = title || (data ? `${langName(data.src)} ${arrow()} ${langName(data.tl)}` : "");
    const bar = h("div", { class: "bar" },
      h("div", { class: "brand" }, h("span", { class: "dot" }, icon("translate", 11, 2.6)), L("common.lamha")),
      langLabel && h("span", { class: "lang" }, langLabel),
      data && data.source === "local" && h("span", { class: "badge", title: L("c.localBadgeTitle") }, icon("book", 11, 2.4), L("c.localBadge")),
      data && data.source === "ai" && h("span", { class: "badge", title: L("c.aiBadgeTitle", { p: data.ai }) }, icon("sparkle", 11, 2.4), data.ai),
      h("div", { class: "spacer" }),
      stack.length > 1 && h("button", { class: "icon-btn", title: L("common.back"), "aria-label": L("common.back"), onclick: goBack }, icon("back")),
      h("button", { class: "icon-btn", title: L("common.settings"), "aria-label": L("common.settings"), onclick: () => send({ type: "openOptions" }) }, icon("settings")),
      h("button", { class: "icon-btn", title: L("c.closeEsc"), "aria-label": L("common.close"), onclick: closeCard }, icon("close"))
    );
    bodyEl = h("div", { class: "body" });
    card.append(bar, bodyEl);
    return bodyEl;
  }

  function footer(data) {
    const q = encodeURIComponent(data.query);
    const links = [
      h("a", { href: `https://translate.google.com/?sl=${encodeURIComponent(data.src || "auto")}&tl=${encodeURIComponent(data.tl)}&text=${q}&op=translate`, target: "_blank", rel: "noopener noreferrer" }, icon("external", 13), L("c.google"))
    ];
    if (data.type === "word" && data.src === "en" && data.tl === "ar") {
      links.push(h("a", { href: `https://dictionary.cambridge.org/dictionary/english-arabic/${encodeURIComponent(data.query.toLowerCase().replace(/ /g, "-"))}`, target: "_blank", rel: "noopener noreferrer" }, icon("external", 13), L("c.cambridge")));
    }
    const tr = aiTranslator();
    if (tr.ready && data.type === "text" && data.source !== "ai") { // Google's answer: the AI can do idioms and slang better
      const text = data.query;
      links.push(h("a", { href: "#", role: "button", title: L("c.betterTrTitle", { p: tr.name }), onclick: e => { e.preventDefault(); load(text, null, { engine: "ai" }); } },
        icon("sparkle", 13), L("c.betterTr")));
    }
    if (aiReady && data.type === "text" && cardInfo) {
      const info = cardInfo;
      links.push(h("a", { href: "#", role: "button", onclick: e => { e.preventDefault(); openWriteCard(info); } }, icon("sparkle", 13), L("p.writeTools")));
    }
    card.append(h("div", { class: "foot" }, links));
  }

  /* ----- loading / error ----- */

  function renderSkeleton(text) {
    const b = frame(null);
    const word = isWordish(text);
    b.append(
      word
        ? h("div", { class: "head" }, h("div", { class: "w" }, h("div", { class: "headword", dir: "auto" }, text)))
        : h("div", { class: "source", dir: "auto" }, text),
      h("div", { class: "sk", style: { height: word ? "52px" : "72px", marginTop: "12px", borderRadius: "11px" } }),
      word && h("div", { class: "sk", style: { height: "12px", width: "40%", marginTop: "20px" } }),
      word && h("div", { class: "sk", style: { height: "12px", width: "90%", marginTop: "10px" } }),
      word && h("div", { class: "sk", style: { height: "12px", width: "75%", marginTop: "8px" } })
    );
  }

  function renderError(text, err, opts = {}) {
    const b = frame(null);
    if (err === "too_long") { // no retry: the same text would fail again
      const [title, hint] = LamhaAI.errorInfo("ai_too_long");
      b.append(h("div", { class: "err" }, h("b", null, title), hint));
      return;
    }
    const code = String(err || "");
    if ((Object.hasOwn(LamhaAI.ERRORS, code) && code !== "network") || code.startsWith("ai_error:")) { // the AI translator failed (quota, key…)
      const [title, hint, needsSettings] = LamhaAI.errorInfo(code, aiTranslator().name);
      b.append(h("div", { class: "err" }, h("b", null, title), hint, h("div", null, needsSettings
        ? h("button", { class: "btn", onclick: () => send({ type: "openOptions", section: "translation" }) }, icon("settings", 14), L("common.settings"))
        : h("button", { class: "btn", onclick: () => load(text, null, opts) }, icon("retry", 14), L("common.retry")))));
      return;
    }
    const offline = !navigator.onLine;
    const busy = err === "rate_limited";
    const notFound = err === "not_found_offline", offlineMode = err === "offline_mode";
    b.append(h("div", { class: "err" },
      h("b", null, notFound ? L("c.errNotFound") : offlineMode ? L("c.errOfflineMode")
        : offline ? L("c.errOffline") : busy ? L("c.errBusy") : L("c.errFailed")),
      notFound || offlineMode ? L("c.errLocalOnly")
        : offline ? L("c.errCheckConn")
        : busy ? L("c.errTooMany")
        : L("c.errService"),
      h("div", null, h("button", { class: "btn", onclick: () => load(text, stack.length < 2 && cardInfo ? cardInfo.context : null) }, icon("retry", 14), L("common.retry")))
    ));
  }

  /* ----- lookup ----- */

  /** Runs `fn` (which redraws the card) so the card glides to its new height instead of jumping. */
  function morph(fn) {
    const c = card, before = c ? c.getBoundingClientRect().height : 0;
    const out = fn();
    if (c && card === c) LamhaMotion.resize(c, before, { duration: 220 });
    return out;
  }

  /** `opts.engine: "ai"`: the "Better translation" link, which asks the AI whatever the translation setting is. */
  async function load(text, context = null, opts = {}) {
    const token = ++reqId;
    renderSkeleton(text);
    card.classList.add("thinking"); // the Lamha mark blinks until the answer is here
    const res = await send({ type: "lookup", text, context, engine: opts.engine });
    if (token !== reqId || !card) return;
    card.classList.remove("thinking");
    if (!res || !res.ok) { morph(() => renderError(text, res && res.error, opts)); return; }
    const pending = morph(() => render(res.data));
    if (res.data.milestone) LamhaMotion.burst(card.querySelector(".milestone"), { layer: root });
    if (settings.autoSpeak && res.data.type === "word") speak(res.data.query, res.data.src, null);
    if (pending.length) fillGlosses(pending, token);
    if (settings.dictSource !== "offline" && settings.showWikipedia && res.data.type === "word" && res.data.src === "en" && res.data.query.length > 2) {
      const w = await send({ type: "wiki", title: res.data.query, lang: res.data.tl === "en" ? "en" : settings.targetLang });
      if (token !== reqId || !bodyEl || !w || !w.data) return;
      bodyEl.append(wikiSection(w.data));
    }
  }

  /** Local results show English definitions at once; Arabic versions are filled in when they arrive. */
  async function fillGlosses(pending, token) {
    const res = await send({ type: "translateBatch", texts: pending.map(p => p.gloss), tl: "ar", sl: "en", kind: "gloss" });
    if (token !== reqId || !res || !res.ok) { pending.forEach(p => p.el.remove()); return; }
    pending.forEach((p, i) => {
      if (res.data[i]) { p.el.textContent = res.data[i]; p.el.classList.remove("sk-line"); }
      else p.el.remove();
    });
  }

  function navigate(word) {
    stack.push(word);
    load(word);
  }
  function goBack() {
    if (stack.length < 2) return;
    stack.pop();
    load(stack[stack.length - 1], stack.length === 1 && cardInfo ? cardInfo.context : null);
  }

  /** Text clamped to a few lines that opens fully on click, Enter or Space. */
  function expandable(props, ...kids) {
    const el = h("div", { ...props, role: "button", tabindex: "0", "aria-expanded": "false", title: L("c.showAll") }, ...kids);
    const toggle = () => el.setAttribute("aria-expanded", String(el.classList.toggle("open")));
    el.addEventListener("click", toggle);
    el.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); toggle(); } });
    return el;
  }

  /* ----- result rendering ----- */

  function render(d) {
    const b = frame(d);
    const pending = [];
    const tDir = dirOf(d.tl), sDir = dirOf(d.src);
    if (d.milestone) b.append(h("div", { class: "milestone", role: "status" }, L("ms.lookups", { n: d.milestone })));

    if (d.type === "word") {
      const speakBtn = h("button", { class: "speak", title: L("c.listenPron"), "aria-label": L("c.listenPron"), onclick: e => speak(d.query, d.src, e.currentTarget) }, icon("speak", 17));
      b.append(h("div", { class: "head", dir: sDir },
        h("div", { class: "w" },
          h("div", { class: "headword" + (sDir === "rtl" ? " ar" : "") }, d.query),
          d.srcTranslit && h("div", { class: "phon" }, `/${d.srcTranslit}/`),
          d.inflected && h("div", { class: "form-of" }, h("span", { dir: "ltr" }, d.inflected), L("c.formOf"), h("b", { dir: "ltr" }, d.query)),
          !d.inflected && d.formOf && h("div", { class: "form-of" }, L("c.formOfPlain"),
            h("button", { class: "link", dir: "ltr", title: L("c.lookUpX", { w: d.formOf }), onclick: () => navigate(d.formOf) }, d.formOf))
        ),
        d.src === "en" && d.translation && reviewBtn(d),
        speakBtn
      ));
    } else {
      b.append(h("div", { class: "src-row", dir: sDir },
        expandable({ class: "source", dir: sDir }, d.query),
        h("button", { class: "icon-btn", title: L("c.listenOrig"), "aria-label": L("c.listenOrig"), onclick: e => speak(d.query, d.src, e.currentTarget) }, icon("speak", 15))
      ));
    }

    if (d.spell) {
      b.append(h("div", { class: "spell" }, L("c.didYouMean"), h("button", { onclick: () => navigate(d.spell) }, d.spell), L("c.didYouMeanEnd")));
    }

    // hero translation — the meaning in this sentence when we know it
    const ctx = d.context && d.context.word ? d.context : null;
    const main = ctx ? ctx.word : d.translation;
    const long = main.length > 40;
    b.append(h("div", { class: "hero", dir: "rtl" },
      h("div", { style: { flex: "1", minWidth: "0" } },
        (ctx || d.contextSense) && h("div", { class: "ctx-label" }, ctx && ctx.untranslated ? L("c.inContextName") : L("c.inContext")),
        main
          ? h("div", { class: "t" + (long ? " long" : ""), dir: ctx && ctx.untranslated ? "auto" : tDir }, main)
          : h("div", { class: "t none" }, d.bestGloss
              ? [L("c.noArSense"), h("span", { class: "gloss", dir: "ltr" }, d.bestGloss)]
              : L("c.noArDirect")),
        !ctx && d.type === "word" && d.translit && tDir === "rtl" && h("div", { class: "tr" }, d.translit),
        ctx && (ctx.pre || ctx.post) && h("div", { class: "ctx", dir: tDir }, ctx.pre, h("mark", null, ctx.word), ctx.post)
      ),
      !!main && h("div", { class: "actions" },
        h("button", { class: "icon-btn", title: L("common.listen"), "aria-label": L("c.listenTr"), onclick: e => speak(main, d.tl, e.currentTarget) }, icon("speak", 15)),
        h("button", { class: "icon-btn", title: L("common.copy"), "aria-label": L("c.copyTr"), onclick: () => copyText(main) }, icon("copy", 15))
      )
    ));

    // alternative meanings by part of speech
    if (d.dict && d.dict.length) {
      const sec = h("div", { class: "sec" }, h("div", { class: "sec-h" }, L("c.otherMeanings")));
      for (const p of d.dict.slice(0, 5)) {
        if (p.terms.some(t => t.hint)) { sec.append(senseGroups(p, d)); continue; }
        const shown = p.terms.slice(0, 7), rest = p.terms.slice(7);
        const chips = h("span", { class: "chips" }, shown.map(t => termChip(t, d)));
        if (rest.length) {
          const more = h("button", { class: "chip more", dir: "ltr", title: L("c.showMore"), onclick: () => { more.remove(); chips.append(...rest.map(t => termChip(t, d))); } }, `+${rest.length}`);
          chips.append(more);
        }
        sec.append(h("div", { class: "pos-row" }, h("span", { class: "pos" }, p.pos), chips));
      }
      b.append(sec);
    }

    // definitions
    if (d.definitions && d.definitions.length) {
      const sec = h("div", { class: "sec" }, h("div", { class: "sec-h" }, L("c.definition")));
      for (const p of d.definitions) {
        sec.append(h("div", { class: "pos-row", style: { marginBottom: "6px" } }, h("span", { class: "pos" }, p.pos)));
        sec.append(h("ol", { class: "defs" }, p.entries.map(e => h("li", { class: "def" + (e.best ? " best" : ""), title: e.best ? L("c.bestSense") : null },
          e.ar && e.ar.length > 0 && h("div", { class: "ar-w", dir: "rtl" }, e.ar.join(" · ")),
          e.glossTr && h("div", { class: "ar-g", dir: tDir }, e.glossTr),
          !e.glossTr && d.source === "local" && tDir === "rtl" && settings.translateDefinitions && settings.dictSource !== "offline" && pendingGloss(pending, e.gloss),
          h("div", { class: "en-g", dir: "ltr" }, e.gloss),
          e.example && h("div", { class: "ex", dir: "ltr" }, `“${e.example}”`),
          e.synonyms && e.synonyms.length > 0 && h("div", { class: "syn" }, e.synonyms.map(s =>
            h("button", { class: "chip en", title: L("c.lookUpX", { w: s }), onclick: () => navigate(s) }, s)))
        ))));
      }
      b.append(sec);
    } else if (d.examples && d.examples.length) {
      b.append(h("div", { class: "sec" }, h("div", { class: "sec-h" }, L("c.examples")),
        h("ul", { class: "examples" }, d.examples.map(x => h("li", null, x)))));
    }

    footer(d);
    return pending;
  }

  /** Bookmark: is this word in the review deck? Looked-up words are usually added automatically. */
  function reviewBtn(d) {
    const btn = h("button", { class: "icon-btn mark", "aria-pressed": "false" }, icon("bookmark", 17));
    const show = on => {
      btn.classList.toggle("on", on);
      btn.setAttribute("aria-pressed", String(on));
      btn.title = on ? L("c.inReview") : L("c.addReview");
      btn.setAttribute("aria-label", btn.title);
    };
    show(false);
    const token = reqId;
    send({ type: "cardHas", q: d.query }).then(on => { if (token === reqId) show(on === true); });
    btn.addEventListener("click", async () => {
      const c = stack.length === 1 && cardInfo && cardInfo.context ? cardInfo.context : null;
      const def = d.definitions && d.definitions[0] && d.definitions[0].entries[0];
      const on = await send({
        type: "cardToggle",
        card: {
          q: d.query, tr: (d.context && !d.context.untranslated && d.context.word) || d.translation, form: stack.length === 1 && cardInfo ? cardInfo.text : "",
          ex: c ? (c.before + cardInfo.text + c.after).replace(/\s+/g, " ").trim().slice(0, 300) : "", def: def ? def.gloss : ""
        }
      });
      show(on === true);
      toast(on === true ? L("c.addedReview") : L("c.removedReview"));
    });
    return btn;
  }

  function pendingGloss(pending, gloss) {
    if (pending.length >= 14) return null;
    const el = h("div", { class: "ar-g sk-line", dir: "rtl" });
    pending.push({ el, gloss });
    return el;
  }

  /** Arabic meanings grouped by the English sense they belong to (offline dictionary). */
  function senseGroups(p, d) {
    const groups = [];
    for (const t of p.terms) {
      let g = groups.find(x => x.hint === (t.hint || ""));
      if (!g) { g = { hint: t.hint || "", terms: [] }; groups.push(g); }
      g.terms.push(t);
    }
    const wrap = h("div", { class: "pos-row" }, h("div", { class: "pos" }, p.pos));
    const rows = groups.map(g => h("div", { class: "sense" + (g.terms.some(t => t.best) ? " best" : "") },
      h("span", { class: "chips" }, g.terms.map(t => termChip(t, d))),
      g.hint && h("span", { class: "hint", dir: "ltr" }, g.hint)
    ));
    const rest = rows.splice(4);
    wrap.append(...rows);
    if (rest.length) {
      const more = h("button", { class: "chip more", onclick: () => { more.remove(); wrap.append(...rest); } }, L("c.moreMeanings", { n: rest.length }));
      wrap.append(more);
    }
    return wrap;
  }

  function termChip(t, d) {
    return h("button", {
      class: "chip" + (dirOf(d.tl) === "ltr" ? " en" : ""),
      title: t.back && t.back.length ? t.back.join(dirOf(d.src) === "rtl" ? "، " : ", ") : t.hint || L("common.copy"), // back-translations: source language
      onclick: () => (dirOf(d.tl) === "ltr" ? navigate(t.word) : copyText(t.word))
    }, t.word);
  }

  function wikiSection(w) {
    const xDir = dirOf(w.lang);
    return h("div", { class: "sec" },
      h("div", { class: "sec-h" }, L("c.wikipedia")),
      h("div", { class: "wiki", dir: "rtl" },
        w.thumb && h("img", { src: w.thumb, alt: "", loading: "lazy", referrerpolicy: "no-referrer", onerror: e => e.currentTarget.remove() }),
        h("div", { style: { minWidth: "0" } },
          h("div", { class: "x", dir: xDir }, w.extract),
          w.url && h("a", { href: w.url, target: "_blank", rel: "noopener noreferrer" }, L("c.readWiki"), icon("external", 12))
        )
      )
    );
  }

  /* ---------------- writing tools (Claude, Gemini or Ollama) ---------------- */

  const toolLabel = id => L("tool." + id); // proofread, improve, formal, friendly, concise, toEnglish, summarize, explain, reply
  const REPLACEABLE = new Set(["proofread", "improve", "formal", "friendly", "concise", "toEnglish", "compose"]);
  const { isArabicText } = LamhaAI;

  let writeOut = null;
  let writeTools = []; // the card's tools in order: 1…n on the keyboard runs one

  /** 1–9 (or ١–٩) in the writing card runs that tool: Alt+Shift+W, then 1 = proofread. */
  function onWriteKey(e) {
    if (e.ctrlKey || e.altKey || e.metaKey || !writeTools.length || /^(TEXTAREA|INPUT)$/.test(e.target.tagName)) return;
    const latin = "123456789".indexOf(e.key), arabic = "١٢٣٤٥٦٧٨٩".indexOf(e.key);
    const i = latin >= 0 ? latin : arabic;
    if (i < 0 || i >= writeTools.length) return;
    e.preventDefault();
    e.stopPropagation(); // the page never sees the number
    flashChip(writeTools[i]);
    pickTool(writeTools[i]);
  }

  /** The chip whose number was pressed lights up for a moment, so it's clear which tool the key picked. */
  function flashChip(tool) {
    const chip = card && card.querySelector(`.chip.tool[data-tool="${tool}"]`);
    if (!chip) return;
    chip.classList.add("hit");
    setTimeout(() => chip.classList.remove("hit"), 380);
    LamhaMotion.play(chip, [{ transform: "none" }, { transform: "scale(1.14)", offset: 0.35 }, { transform: "none" }], { duration: 280, easing: "ease-out", fill: "none" });
  }

  function openWriteCard(info, { focus = false, tool = null } = {}) {
    if (!info) return;
    ensureHost();
    hidePill();
    closeCard({ restore: false, animate: false });
    if (focus) returnFocus = document.activeElement;
    if (!info.raw) info.raw = info.text;
    cardInfo = info;
    card = h("div", { class: "card write", role: "dialog", "aria-label": L("c.writeLabel"), tabindex: "-1", onkeydown: onWriteKey });
    root.append(card);
    decideSide();
    if (info.compose) renderCompose();
    else renderWrite(info, tool);
    placeCard();
    if (focus && !info.compose) card.focus({ preventScroll: true }); // compose focuses its own text box
    if (!aiReady) writeOut.replaceChildren(aiError(notReadyCode()));
    else if (tool) pickTool(tool);
  }

  const notReadyCode = () => aiProvider().notReady;

  function renderWrite(info, active) {
    const b = frame(null, L("c.writeTitle", { p: aiProvider().name }));
    const src = info.raw.trim();
    const tools = info.page ? ["summarize"]
      : isArabicText(src) ? ["toEnglish", "reply", "summarize"]
      : ["proofread", "improve", "formal", "friendly", "concise", "summarize", "explain", "reply"];
    writeTools = tools;
    b.append(
      expandable({ class: "source w-src", dir: isArabicText(src) ? "rtl" : "ltr" },
        info.page ? document.title || location.hostname : src),
      h("div", { class: "tools", role: "toolbar", "aria-label": L("p.writeTools") }, tools.map((id, i) =>
        h("button", {
          class: "chip tool" + (id === active ? " on" : ""), "data-tool": id, "aria-pressed": String(id === active),
          "aria-keyshortcuts": String(i + 1), title: L("c.toolKey", { n: i + 1 }), onclick: () => pickTool(id)
        }, h("span", { class: "num", "aria-hidden": "true" }, LamhaI18n.num(i + 1)), toolLabel(id))))
    );
    writeOut = h("div", { class: "w-out", "aria-live": "polite" });
    b.append(writeOut);
  }

  function markTool(tool) {
    if (!card) return;
    card.querySelectorAll(".tool").forEach(c => {
      const on = c.dataset.tool === tool;
      c.classList.toggle("on", on);
      c.setAttribute("aria-pressed", String(on));
    });
  }

  function pickTool(tool) {
    if (!aiReady) { writeOut.replaceChildren(aiError(notReadyCode())); return; }
    if (tool === "reply") replyForm();
    else runTool(tool, tool === "summarize" ? { lang: LamhaI18n.lang() } : {}); // in the interface language; the card offers the other one
  }

  async function runTool(tool, extra = {}) {
    const token = ++reqId;
    const info = cardInfo;
    markTool(tool);
    writeOut.replaceChildren(
      h("div", { class: "sk", style: { height: "14px", width: "92%" } }),
      h("div", { class: "sk", style: { height: "14px", width: "80%", marginTop: "8px" } }),
      h("div", { class: "sk", style: { height: "14px", width: "60%", marginTop: "8px" } })
    );
    card.classList.add("thinking");
    const res = await send({ type: "ai", tool, text: tool === "compose" ? extra.intent : info.raw, extra });
    if (token !== reqId || !card) return;
    card.classList.remove("thinking");
    morph(() => {
      if (!res || !res.ok) writeOut.replaceChildren(aiError(res && res.error, () => runTool(tool, extra)));
      else if (tool === "proofread") renderProofread(res.data, info);
      else renderAiText(res.data.text || "", tool, extra);
      placeCard();
    });
  }

  function aiError(code, retry) {
    const [title, text, needsSettings] = LamhaAI.errorInfo(code, aiProvider().name);
    return h("div", { class: "err" },
      h("b", null, title), text,
      h("div", null,
        needsSettings
          ? h("button", { class: "btn", onclick: () => send({ type: "openOptions", section: "ai" }) }, icon("settings", 14), L("common.settings"))
          : retry && h("button", { class: "btn", onclick: retry }, icon("retry", 14), L("common.retry")))
    );
  }

  function resultActions(text, tool, extra) {
    const canReplace = REPLACEABLE.has(tool) && cardInfo && cardInfo.editable;
    return h("div", { class: "w-actions" },
      canReplace && h("button", { class: "btn", onclick: () => doReplace(text, tool === "compose") }, icon("check", 14), tool === "compose" ? L("c.insert") : L("c.replace")),
      h("button", { class: "btn" + (canReplace ? " ghost" : ""), onclick: () => copyText(text) }, icon("copy", 14), L("common.copy")),
      h("div", { class: "spacer" }),
      tool === "summarize" && h("button", { class: "btn ghost", onclick: () => runTool(tool, { lang: extra.lang === "en" ? "ar" : "en" }) },
        extra.lang === "en" ? L("c.inArabic") : L("c.inEnglish")),
      tool === "reply" && h("button", { class: "btn ghost", onclick: () => replyForm(extra) }, L("c.editRequest")),
      tool === "compose" && h("button", { class: "btn ghost", onclick: () => composeForm(extra) }, L("c.editRequest")),
      h("button", { class: "icon-btn", title: L("write.again"), "aria-label": L("write.again"), onclick: () => runTool(tool, { ...extra, fresh: true }) }, icon("retry", 15))
    );
  }

  function renderAiText(text, tool, extra) {
    const box = h("div", { class: "w-text", dir: "auto" }, text);
    writeOut.replaceChildren(box, resultActions(text, tool, extra));
    LamhaMotion.typeIn(box); // word by word, full animations only
  }

  function renderProofread(data, info) {
    const src = info.raw.trim();
    const issues = Array.isArray(data.issues) ? data.issues : [];
    const corrected = String(data.corrected || "").trim();
    if (!issues.length || !corrected || corrected === src) {
      const ok = h("div", { class: "w-ok" }, icon("check", 16, 2.6), L("write.noErrors"));
      writeOut.replaceChildren(ok);
      requestAnimationFrame(() => LamhaMotion.burst(ok, { layer: root, count: 10, glyphs: ["✓", "✦", "•"] }));
      return;
    }
    const diff = h("div", { class: "w-text", dir: "ltr" }, LamhaAI.diffNodes(h, src, corrected));
    LamhaMotion.sequence(diff, "del, ins"); // each mistake struck through, then its fix
    writeOut.replaceChildren(
      diff,
      resultActions(corrected, "proofread", {}),
      h("div", { class: "sec" },
        h("div", { class: "sec-h" }, L("c.corrections", { n: issues.length })),
        h("ul", { class: "issues" }, issues.map(i => h("li", null,
          h("div", { class: "fix", dir: "ltr" }, h("del", null, i.original), " → ", h("ins", null, i.fix)),
          i.category !== "other" && h("span", { class: "cat" }, LamhaAI.catLabel(i.category)),
          h("div", { class: "why", dir: "auto" }, i.why)
        ))))
    );
  }

  function replyForm(prev = {}) {
    ++reqId; // cancel a tool that is still running
    markTool("reply");
    const input = h("textarea", {
      class: "w-input", rows: "2", dir: "auto",
      placeholder: L("c.replyPlaceholder")
    });
    input.value = prev.intent || "";
    // keep site shortcuts (Gmail, Slack…) from reacting to what is typed here
    ["keydown", "keyup", "keypress"].forEach(t => input.addEventListener(t, e => { if (e.key !== "Escape") e.stopPropagation(); }));
    let tone = prev.tone || "";
    const tones = h("div", { class: "tools" }, [["", L("c.toneAuto")], ["friendly", L("tool.friendly")], ["formal", L("tool.formal")], ["short", L("c.toneShort")]].map(([v, label]) =>
      h("button", {
        class: "chip" + (v === tone ? " on" : ""),
        onclick: e => { tone = v; tones.querySelectorAll(".chip").forEach(c => c.classList.toggle("on", c === e.currentTarget)); }
      }, label)));
    const go = () => runTool("reply", { intent: input.value, tone });
    input.addEventListener("keydown", e => { if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); go(); } });
    writeOut.replaceChildren(
      input,
      h("div", { class: "w-note" }, L("c.tone")),
      tones,
      h("button", { class: "btn", onclick: go }, icon("sparkle", 14), L("c.writeReply"))
    );
    input.focus({ preventScroll: true });
    placeCard();
  }

  /* ----- write new: nothing selected, the user describes what to write ----- */

  function renderCompose() {
    const b = frame(null, L("c.composeTitle", { p: aiProvider().name }));
    writeTools = [];
    writeOut = h("div", { class: "w-out", "aria-live": "polite" });
    b.append(writeOut);
    if (aiReady) composeForm();
  }

  function composeForm(prev = {}) {
    ++reqId; // cancel a request still running
    const input = h("textarea", { class: "w-input", rows: "3", dir: "auto", placeholder: L("c.composePlaceholder"), "aria-label": L("c.composeLabel") });
    input.value = prev.intent || "";
    // keep site shortcuts (Gmail, Slack…) from reacting to what is typed here
    ["keydown", "keyup", "keypress"].forEach(t => input.addEventListener(t, e => { if (e.key !== "Escape") e.stopPropagation(); }));
    let kind = prev.kind || "message", tone = prev.tone || "";
    const choice = (options, value, set) => {
      const row = h("div", { class: "tools", role: "group" }, options.map(([v, label]) =>
        h("button", {
          class: "chip" + (v === value ? " on" : ""), "aria-pressed": String(v === value),
          onclick: e => { set(v); row.querySelectorAll(".chip").forEach(c => { const on = c === e.currentTarget; c.classList.toggle("on", on); c.setAttribute("aria-pressed", String(on)); }); }
        }, label)));
      return row;
    };
    const go = () => {
      if (!input.value.trim()) { input.focus(); return; }
      runTool("compose", { intent: input.value.trim(), kind, tone });
    };
    input.addEventListener("keydown", e => { if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); go(); } });
    writeOut.replaceChildren(
      input,
      h("div", { class: "w-note" }, L("c.kind")),
      choice([["message", L("c.kindMessage")], ["email", L("c.kindEmail")]], kind, v => { kind = v; }),
      h("div", { class: "w-note" }, L("c.tone")),
      choice([["", L("c.toneAuto")], ["friendly", L("tool.friendly")], ["formal", L("tool.formal")], ["short", L("c.toneShort")]], tone, v => { tone = v; }),
      h("button", { class: "btn", onclick: go }, icon("sparkle", 14), L("c.composeGo"), h("span", { class: "kbd" }, "Ctrl+Enter"))
    );
    input.focus({ preventScroll: true });
    placeCard();
  }

  /** Where "Write new" inserts its text: the caret in the focused text box or editor, if there is one. */
  function composeTarget() {
    const ae = document.activeElement;
    if (isTextField(ae)) {
      const s = ae.selectionStart ?? ae.value.length, e = ae.selectionEnd ?? s;
      return { kind: "field", el: ae, start: s, end: e, original: ae.value.slice(s, e) };
    }
    if (ae && ae.isContentEditable) {
      const sel = window.getSelection();
      if (sel && sel.rangeCount) {
        const range = sel.getRangeAt(0).cloneRange();
        return { kind: "rich", el: editingHost(ae), range, original: range.toString() };
      }
    }
    return undefined;
  }

  function composeInfo(msg) {
    const ae = document.activeElement;
    return {
      compose: true, text: "", raw: "",
      point: msg.point || (isTextField(ae) ? fieldPoint(ae) : lastPointer) || { x: innerWidth / 2, y: 90 },
      editable: msg.external ? (msg.replaceable ? { kind: "external" } : undefined) : composeTarget()
    };
  }

  /** Puts `text` back where the selection was, keeping the editor's undo history when possible. */
  function replaceIn(ed, text) {
    try {
      if (ed && ed.kind === "external") return !!(window.lamhaDesktop && window.lamhaDesktop.replace(text)); // desktop app
      if (!ed || !ed.el.isConnected) return false;
      if (ed.kind === "field") {
        const el = ed.el;
        if (el.value.slice(ed.start, ed.end) !== ed.original) return false; // edited meanwhile
        el.focus({ preventScroll: true });
        el.setSelectionRange(ed.start, ed.end);
        if (!document.execCommand("insertText", false, text)) {
          el.setRangeText(text, ed.start, ed.end, "end");
          el.dispatchEvent(new Event("input", { bubbles: true }));
        }
        return true;
      }
      if (ed.range.toString() !== ed.original) return false;
      ed.el.focus({ preventScroll: true });
      const sel = window.getSelection();
      sel.removeAllRanges();
      sel.addRange(ed.range);
      if (!document.execCommand("insertText", false, text)) {
        ed.range.deleteContents();
        ed.range.insertNode(document.createTextNode(text));
        ed.el.dispatchEvent(new Event("input", { bubbles: true }));
      }
      return true;
    } catch (_) { return false; }
  }

  function doReplace(text, inserting = false) {
    const info = cardInfo;
    const raw = info.raw || "";
    const full = raw.match(/^\s*/)[0] + text + raw.match(/\s*$/)[0]; // keep the spaces around the selection
    const gone = closeCard({ send: true }); // the card shrinks back into the text it came from
    // desktop app: its window hides for the paste, so the card finishes leaving first (≤150 ms, none when animations are off)
    const ready = info.editable && info.editable.kind === "external" ? gone : Promise.resolve();
    ready.then(() => {
      if (replaceIn(info.editable, full)) toast(inserting ? L("c.inserted") : L("c.replaced"), true);
      else copyText(text).then(() => toast(L("c.replaceFailed")));
    });
  }

  function summarizePage() {
    const main = document.querySelector("article, main, [role=main]") || document.body;
    const text = (main.innerText || "").replace(/\n{3,}/g, "\n\n").trim().slice(0, 45000);
    if (!LETTER_RE.test(text)) { toast(L("c.nothingToSummarize")); return; }
    openWriteCard({ text: document.title, raw: text, page: true, point: { x: innerWidth / 2, y: 40 } }, { focus: true, tool: "summarize" });
  }

  /* ---------------- page-translation bar (top frame) ---------------- */

  let pbar = null, pbarIdle, pbarKey = "";
  function renderPageBar(state) {
    if (!IS_TOP) return;
    const key = `${state.active}|${state.loading}|${state.showingOriginal}|${state.failed}`;
    if (key === pbarKey && (pbar || !state.active)) return;
    pbarKey = key;
    if (!state.active) { if (pbar) pbar.remove(); pbar = null; return; }
    ensureHost();
    if (!pbar) { pbar = h("div", { class: "pbar", role: "toolbar", "aria-label": L("c.pageBar") }); root.append(pbar); }
    pbar.textContent = "";
    const busy = state.loading;
    const partial = !busy && state.failed; // some batches failed even after the retries
    const relay = action => send({ type: "relayPage", action });
    pbar.append(
      h("div", { class: "status", role: "status" },
        busy ? h("span", { class: "spin" }) : partial ? h("span", { class: "warn" }, icon("close", 15, 2.6)) : h("span", { class: "ok" }, icon("check", 15, 2.6)),
        busy ? L("common.translating") : partial ? L("c.pagePartial") : L("c.pageDone")
      ),
      partial && h("button", { class: "icon-btn", title: L("common.retry"), "aria-label": L("common.retry"), onclick: () => relay("retry") }, icon("retry", 15)),
      h("div", { class: "seg" },
        h("button", { class: state.showingOriginal ? "" : "on", onclick: () => relay("translated") }, langName(settings.targetLang)),
        h("button", { class: state.showingOriginal ? "on" : "", onclick: () => relay("original") }, L("c.original"))
      ),
      h("button", { class: "icon-btn", title: L("c.stopTranslation"), "aria-label": L("c.stopTranslation"), onclick: () => relay("stop") }, icon("close", 15))
    );
    pbar.classList.remove("mini");
    clearTimeout(pbarIdle);
    if (!busy && !partial) pbarIdle = setTimeout(() => pbar && pbar.classList.add("mini"), 2500); // a failure stays readable
  }
  LamhaPage.onState(renderPageBar);

  function pageAction(action) {
    if (action === "start") LamhaPage.start(settings.targetLang);
    else if (action === "stop") LamhaPage.stop();
    else if (action === "toggle") LamhaPage.isActive() ? LamhaPage.stop() : LamhaPage.start(settings.targetLang);
    else if (action === "original") LamhaPage.showOriginal(true);
    else if (action === "translated") LamhaPage.showOriginal(false);
    else if (action === "retry") LamhaPage.retry();
  }

  /* ---------------- events ---------------- */

  function evaluate(e) {
    if (!isActiveHere()) return;
    const info = getSelectionInfo();
    if (!info) { hidePill(); return; }
    if (card && cardInfo && cardInfo.text === info.text) return;
    const mode = settings.triggerMode;
    if (info.aiOnly) showPill(info); // the writing tools are never opened unasked
    else if (mode === "instant" || (e && e.altKey)) openCard(info);
    else if (mode === "button") showPill(info);
    // "modifier" mode: only Alt+select opens (handled above)
  }

  document.addEventListener("pointerup", e => {
    if (fromUs(e) || e.button > 0) return;
    lastPointer = { x: e.clientX, y: e.clientY };
    setTimeout(() => evaluate(e), 10);
  }, true);

  document.addEventListener("pointerdown", e => {
    if (fromUs(e)) return;
    hidePill();
    if (card) closeCard({ restore: false });
  }, true);

  document.addEventListener("keydown", e => {
    if (e.key === "Escape" && (card || pill)) { closeAll(); return; }
    if (pill && !e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey) hidePill();
  }, true);

  document.addEventListener("keyup", e => {
    if (fromUs(e)) return;
    const selectAll = (e.ctrlKey || e.metaKey) && (e.key || "").toLowerCase() === "a";
    if (e.shiftKey || e.key === "Shift" || selectAll) { lastPointer = null; evaluate(e); }
  }, true);

  let selTimer;
  document.addEventListener("selectionchange", () => {
    if (!pill) return;
    clearTimeout(selTimer);
    selTimer = setTimeout(() => {
      const s = window.getSelection();
      if ((!s || s.isCollapsed) && !editableSelection()?.text) hidePill();
    }, 120);
  });

  let rafPending = false;
  const onViewportChange = () => {
    if (rafPending || (!pill && !card)) return;
    rafPending = true;
    requestAnimationFrame(() => { rafPending = false; placePill(); placeCard(); });
  };
  window.addEventListener("scroll", onViewportChange, { capture: true, passive: true });
  window.addEventListener("resize", onViewportChange, { passive: true });

  /** Text handed over by the context menu, or by the desktop app from another program (`external`):
   *  there Replace pastes back into that program. */
  function textInfo(msg) {
    const text = msg.text.replace(/\s+/g, " ").trim();
    const ctx = msg.context && typeof msg.context.before === "string" && typeof msg.context.after === "string" && isWordish(text)
      ? sentenceAround(msg.context.before, msg.context.after) : null; // the desktop app read the sentence from the other program
    return {
      text,
      raw: msg.text,
      point: msg.point || lastPointer || { x: innerWidth / 2, y: 90 },
      context: ctx,
      editable: msg.external && msg.replaceable ? { kind: "external" } : undefined
    };
  }

  /* ---------------- messages from background / popup ---------------- */

  browser.runtime.onMessage.addListener(msg => {
    if (!msg) return;
    if (msg.type === "showLookup") {
      if (!msg.external && !document.hasFocus()) return;
      const info = !msg.external && getSelectionInfo(true);
      if (info) { openCard(info, { focus: true }); return; }
      if (msg.text) { openCard(textInfo(msg), { focus: true }); return; }
      toast(L("c.selectFirst"));
    } else if (msg.type === "showWrite") {
      if (!msg.external && !document.hasFocus()) return;
      const info = !msg.external && (getSelectionInfo(true) || wholeEditable());
      if (info) { openWriteCard(info, { focus: true }); return; }
      if (msg.text) { openWriteCard(textInfo(msg), { focus: true }); return; }
      openWriteCard(composeInfo(msg), { focus: true }); // nothing selected: write something new
    } else if (msg.type === "summarizePage") {
      if (IS_TOP) summarizePage();
    } else if (msg.type === "togglePage") {
      pageAction("toggle");
    } else if (msg.type === "pageAction") {
      pageAction(msg.action);
    } else if (msg.type === "pageState") {
      return Promise.resolve({ active: LamhaPage.isActive(), host: location.hostname });
    }
  });
})();
