/* Lamha — whole-page translator.
 * Translates text nodes lazily as they approach the viewport, follows dynamic
 * content, and can flip back to the original instantly.
 */
// eslint-disable-next-line no-unused-vars
var LamhaPage = (() => {
  "use strict";

  const SKIP = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEXTAREA", "INPUT", "SELECT", "OPTION", "CODE", "PRE",
    "KBD", "SAMP", "VAR", "MATH", "SVG", "TEMPLATE", "IFRAME", "OBJECT", "CANVAS", "VIDEO", "AUDIO", "LAMHA-UI"]);
  const DIR_TAGS = new Set(["P", "LI", "H1", "H2", "H3", "H4", "H5", "H6", "BLOCKQUOTE", "DD", "DT", "TD", "TH",
    "FIGCAPTION", "CAPTION", "SUMMARY", "LEGEND"]);
  const INLINE = new Set(["A", "ABBR", "B", "BDI", "BDO", "BIG", "CITE", "DEL", "DFN", "EM", "FONT", "I", "INS",
    "LABEL", "MARK", "NOBR", "Q", "S", "SMALL", "SPAN", "STRONG", "SUB", "SUP", "TIME", "TT", "U"]);
  const ARABIC_RE = /[؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]/;
  const LATIN_RE = /[A-Za-zÀ-ɏ]/;
  const LETTER_RE = /\p{L}/u;
  const BATCH_ITEMS = 40, BATCH_CHARS = 4500, MAX_INFLIGHT = 2;

  let active = false, showingOriginal = false, tl = "ar";
  let orig = new Map();    // Text → original value
  let trans = new Map();   // Text → translated value
  let dirs = new Map();    // Element → previous dir attribute (null = none)
  let byEl = new Map();    // block Element → Set<Text> awaiting visibility
  let queue = new Map();   // block → Set<Text> ready to translate
  let inflight = 0, flushTimer = null, io = null, mo = null, moBuffer = [], moTimer = null;
  let titleOrig = null, titleTrans = null;
  let listener = () => {};
  let rewrites = new WeakMap(); // Text → how often the site rewrote it (live tickers, clocks…)
  const MAX_REWRITES = 3;

  const emit = () => listener({ active, showingOriginal, loading: active && (inflight > 0 || queue.size > 0) });

  function skipElement(el) {
    if (SKIP.has(el.tagName)) return true;
    if (el.getAttribute("translate") === "no" || el.classList.contains("notranslate")) return true;
    if (el.isContentEditable) return true;
    return false;
  }

  function wanted(text) {
    const v = text.nodeValue;
    if (!v || !LETTER_RE.test(v)) return false;
    if (tl === "ar" && ARABIC_RE.test(v) && !LATIN_RE.test(v)) return false; // already Arabic
    return true;
  }

  function collect(rootNode) {
    if (!rootNode) return;
    if (rootNode.nodeType === 3) {
      const p = rootNode.parentElement;
      if (p && !p.closest("script,style,noscript,textarea,code,pre,[translate=no],.notranslate,lamha-ui") && !p.isContentEditable && wanted(rootNode)) register(rootNode);
      return;
    }
    if (rootNode.nodeType !== 1) return;
    if (rootNode.closest && rootNode.closest("[translate=no],.notranslate,lamha-ui")) return;
    const walker = document.createTreeWalker(rootNode, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
      acceptNode(n) {
        if (n.nodeType === 1) return skipElement(n) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_SKIP;
        return wanted(n) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP;
      }
    });
    if (rootNode.nodeType === 1 && skipElement(rootNode)) return;
    let n;
    while ((n = walker.nextNode())) register(n);
  }

  /* Text nodes are grouped by their nearest block ancestor so a sentence split
   * across <a>/<b>/<span>… is translated as one unit (keeps grammar & context). */
  function blockOf(el) {
    while (el && INLINE.has(el.tagName) && el.parentElement) el = el.parentElement;
    return el;
  }

  function register(text) {
    if (orig.has(text)) return;
    const block = blockOf(text.parentElement);
    if (!block) return;
    let set = byEl.get(block);
    if (!set) { set = new Set(); byEl.set(block, set); io.observe(block); }
    set.add(text);
  }

  function onIntersect(entries) {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      const block = entry.target;
      io.unobserve(block);
      const set = byEl.get(block);
      byEl.delete(block);
      if (!set) continue;
      const q = queue.get(block);
      q ? set.forEach(t => q.add(t)) : queue.set(block, set);
    }
    scheduleFlush();
  }

  function scheduleFlush() {
    if (flushTimer) return;
    flushTimer = setTimeout(() => { flushTimer = null; flush(); }, 60);
    emit();
  }

  const esc = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  const byDocOrder = (a, b) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1);

  /** Split a block's text nodes into units: { block, nodes:[{t,v}], q } */
  function unitsFor(block, set) {
    const nodes = [...set].filter(t => t.isConnected && !orig.has(t) && t.nodeValue.trim()).sort(byDocOrder);
    const units = [];
    let cur = [], len = 0;
    for (const t of nodes) {
      const v = t.nodeValue;
      if (cur.length && len + v.length > 1800) { units.push(cur); cur = []; len = 0; }
      cur.push({ t, v }); len += v.length;
    }
    if (cur.length) units.push(cur);
    return units.map(ns => ({
      block, nodes: ns,
      q: ns.length === 1
        ? esc(ns[0].v.trim())
        : ns.map(({ v }, i) => v.match(/^\s*/)[0] + `<a i=${i}>${esc(v.trim())}</a>` + v.match(/\s*$/)[0]).join("")
    }));
  }

  function flush() {
    while (active && queue.size && inflight < MAX_INFLIGHT) {
      const units = [], qs = [], index = new Map();
      let chars = 0;
      for (const [block, set] of queue) {
        queue.delete(block);
        const us = unitsFor(block, set);
        for (const u of us) {
          if (!index.has(u.q)) { index.set(u.q, qs.length); qs.push(u.q); chars += u.q.length; }
          units.push(u);
        }
        if (qs.length >= BATCH_ITEMS || chars >= BATCH_CHARS) break;
      }
      if (!qs.length) continue;
      inflight++;
      browser.runtime.sendMessage({ type: "translateBatch", texts: qs, tl, format: "html" })
        .then(res => {
          if (!active || !res || !res.ok) return;
          for (const u of units) apply(u, res.data[index.get(u.q)]);
        })
        .catch(() => {})
        .finally(() => { inflight--; if (queue.size) scheduleFlush(); emit(); });
    }
    emit();
  }

  const parser = new DOMParser();

  /** Map translated HTML back onto the original text nodes (nodes are kept, only values change). */
  function apply(unit, html) {
    if (!html) return;
    const body = parser.parseFromString(`<body>${html}</body>`, "text/html").body;
    const { nodes } = unit;
    let texts;

    if (nodes.length === 1) {
      texts = [body.textContent];
    } else {
      const segs = [];
      let prefix = "";
      for (const n of body.childNodes) {
        const i = n.nodeType === 1 ? parseInt(n.getAttribute("i"), 10) : NaN;
        if (!Number.isNaN(i) && i >= 0 && i < nodes.length) segs.push({ i, text: n.textContent });
        else if (segs.length) segs[segs.length - 1].text += n.textContent;
        else prefix += n.textContent;
      }
      if (!segs.length) { texts = [body.textContent]; }
      else {
        segs[0].text = prefix + segs[0].text;
        const ordered = segs.every((s, k) => k === 0 || s.i > segs[k - 1].i);
        const hasLink = nodes.some(({ t }) => t.parentElement && t.parentElement.closest("a,button") && unit.block.contains(t.parentElement.closest("a,button")));
        texts = new Array(nodes.length).fill("");
        if (ordered || hasLink) {
          // keep each piece with its original node (link text stays on the link)
          segs.forEach(s => { texts[s.i] += (texts[s.i] ? " " : "") + s.text; });
        } else {
          // target language reordered the phrase: fill nodes in reading order
          segs.forEach((s, k) => { const j = Math.min(k, nodes.length - 1); texts[j] += (texts[j] ? " " : "") + s.text; });
        }
      }
    }

    nodes.forEach(({ t, v }, k) => {
      if (!t.isConnected || t.nodeValue !== v || orig.has(t)) return;
      const core = (texts[k] || "").replace(/\s+/g, " ").trim();
      if (core === v.trim()) return;
      const out = core ? v.match(/^\s*/)[0] + core + v.match(/\s*$/)[0] : (k === 0 ? "" : " ");
      orig.set(t, v);
      trans.set(t, out);
      if (!showingOriginal) t.nodeValue = out;
    });
    setDir(unit.block);
  }

  function setDir(el) {
    if (!el || tl !== "ar") return;
    const block = el.closest("p,li,h1,h2,h3,h4,h5,h6,blockquote,dd,dt,td,th,figcaption,caption,summary,legend");
    if (!block || dirs.has(block) || !DIR_TAGS.has(block.tagName)) return;
    dirs.set(block, block.getAttribute("dir"));
    if (!showingOriginal) block.setAttribute("dir", "auto");
  }

  function onMutations(list) {
    if (!active) return;
    for (const m of list) {
      if (m.type === "characterData") {
        const t = m.target;
        if (trans.get(t) === t.nodeValue || orig.get(t) === t.nodeValue) continue; // our own write
        orig.delete(t); trans.delete(t);
        const n = (rewrites.get(t) || 0) + 1;
        rewrites.set(t, n);
        if (n <= MAX_REWRITES) moBuffer.push(t); // stop chasing text that changes constantly
      } else {
        m.addedNodes.forEach(n => moBuffer.push(n));
      }
    }
    if (moBuffer.length && !moTimer) {
      moTimer = setTimeout(() => {
        moTimer = null;
        const buf = moBuffer; moBuffer = [];
        buf.forEach(n => n.isConnected && collect(n));
        if (Date.now() - lastSweep > SWEEP_MS) sweep();
      }, 250);
    }
  }

  /* Pages that replace their content (feeds, infinite scroll, single-page apps) would otherwise keep every
   * translated node they ever removed alive in these maps until translation stops. A node the site puts back
   * later is collected again like any new content. */
  const SWEEP_MS = 10000;
  let lastSweep = 0;
  function sweep() {
    lastSweep = Date.now();
    for (const t of orig.keys()) if (!t.isConnected) { orig.delete(t); trans.delete(t); }
    for (const block of byEl.keys()) if (!block.isConnected) { io.unobserve(block); byEl.delete(block); }
    for (const el of dirs.keys()) if (!el.isConnected) dirs.delete(el);
  }

  async function translateTitle() {
    const t = document.title;
    if (!t || (ARABIC_RE.test(t) && !LATIN_RE.test(t))) return;
    try {
      const res = await browser.runtime.sendMessage({ type: "translateBatch", texts: [t], tl });
      if (active && res && res.ok && res.data[0]) {
        titleOrig = t; titleTrans = res.data[0];
        if (!showingOriginal) document.title = titleTrans;
      }
    } catch (_) { /* ignore */ }
  }

  function start(lang = "ar") {
    if (active || !document.body) return;
    active = true; showingOriginal = false; tl = lang;
    io = new IntersectionObserver(onIntersect, { rootMargin: "800px 0px 800px 0px" });
    mo = new MutationObserver(onMutations);
    collect(document.body);
    mo.observe(document.body, { childList: true, subtree: true, characterData: true });
    translateTitle();
    emit();
  }

  function stop() {
    if (!active) return;
    active = false;
    io && io.disconnect(); mo && mo.disconnect();
    clearTimeout(flushTimer); flushTimer = null;
    clearTimeout(moTimer); moTimer = null; moBuffer = [];
    for (const [t, v] of orig) if (t.isConnected && t.nodeValue === trans.get(t)) t.nodeValue = v;
    for (const [el, d] of dirs) d == null ? el.removeAttribute("dir") : el.setAttribute("dir", d);
    if (titleOrig && document.title === titleTrans) document.title = titleOrig;
    orig = new Map(); trans = new Map(); dirs = new Map(); byEl = new Map(); queue = new Map(); rewrites = new WeakMap();
    titleOrig = titleTrans = null; showingOriginal = false;
    emit();
  }

  function showOriginal(flag) {
    if (!active || showingOriginal === flag) return;
    showingOriginal = flag;
    const from = flag ? trans : orig, to = flag ? orig : trans;
    for (const [t, v] of to) if (t.isConnected && t.nodeValue === from.get(t)) t.nodeValue = v;
    for (const [el, d] of dirs) {
      if (flag) d == null ? el.removeAttribute("dir") : el.setAttribute("dir", d);
      else el.setAttribute("dir", "auto");
    }
    if (titleOrig) document.title = flag ? titleOrig : titleTrans;
    emit();
  }

  return {
    start, stop, showOriginal,
    isActive: () => active,
    onState: fn => { listener = fn; }
  };
})();
