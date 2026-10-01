/* Lamha — motion: the Animations setting and small helpers every screen uses.
 *
 * Setting (storage.sync `motion`): "auto" | "full" | "subtle" | "off".
 *   auto  → off when the system asks for less motion (Windows: Settings → Accessibility → Animation effects),
 *           otherwise the device's hint (storage.local `motionHint`, written by the desktop app: "subtle" on weak
 *           hardware or without graphics acceleration), otherwise full.
 * The level goes on each attached element as data-motion="full|subtle|off"; the CSS keys off that attribute.
 *
 * Rules the helpers keep: nothing waits on an animation (exit() always settles, with a timeout), only transform and
 * opacity move, and at "off" no animation is created at all. */
// eslint-disable-next-line no-unused-vars
var LamhaMotion = (() => {
  "use strict";
  const SETTINGS = ["auto", "full", "subtle", "off"];
  let setting = "auto", hint = "", level = "full";
  const targets = new Set();
  const listeners = new Set();
  const reduce = typeof matchMedia === "function" ? matchMedia("(prefers-reduced-motion: reduce)") : null;
  const store = typeof browser !== "undefined" && browser.storage ? browser.storage : null;

  function resolve() {
    if (setting !== "auto") return setting;
    if (reduce && reduce.matches) return "off";
    return hint === "subtle" ? "subtle" : "full";
  }

  function apply() {
    const next = resolve();
    const changed = next !== level;
    level = next;
    for (const el of targets) el.setAttribute("data-motion", level);
    if (changed) for (const f of listeners) { try { f(level); } catch (_) { /* a listener's own problem */ } }
  }

  const ready = store
    ? Promise.all([
        store.sync.get({ motion: "auto" }).catch(() => ({})),
        store.local.get({ motionHint: "" }).catch(() => ({}))
      ]).then(([s, l]) => {
        setting = SETTINGS.includes(s.motion) ? s.motion : "auto";
        hint = l.motionHint || "";
        apply();
      })
    : Promise.resolve(apply());

  if (store) {
    store.onChanged.addListener((changes, area) => {
      if (area === "sync" && changes.motion) setting = SETTINGS.includes(changes.motion.newValue) ? changes.motion.newValue : "auto";
      else if (area === "local" && changes.motionHint) hint = changes.motionHint.newValue || "";
      else return;
      apply();
    });
  }
  if (reduce) reduce.addEventListener("change", apply);

  /** Puts data-motion on `el` (a page's <html>, or the card's root inside its shadow DOM) and keeps it current. */
  function attach(el) {
    if (!el) return;
    targets.add(el);
    el.setAttribute("data-motion", level);
  }

  /** Applies a setting right away (Settings shows the result before storage reports the change back). */
  function use(value) {
    if (!SETTINGS.includes(value)) return;
    setting = value;
    apply();
  }

  const any = () => level !== "off";
  const full = () => level === "full";
  /** Subtle keeps motion short: the same animation, a little faster. */
  const time = ms => (level === "subtle" ? Math.round(ms * 0.75) : ms);

  /**
   * el.animate() that respects the level. `fullOnly` animations are skipped at subtle.
   * Returns the Animation, or null when nothing plays.
   */
  function play(el, keyframes, { duration = 180, easing = "cubic-bezier(.2,.8,.2,1)", delay = 0, fill = "backwards", fullOnly = false } = {}) {
    if (!el || !el.animate || !any() || (fullOnly && !full())) return null;
    try { return el.animate(keyframes, { duration: time(duration), easing, delay: time(delay), fill }); }
    catch (_) { return null; }
  }

  /** Children coming in one after another (full only), capped so a long list never waits. */
  function stagger(els, { each = 22, max = 8, duration = 200, distance = 6 } = {}) {
    if (!full()) return;
    [...els].slice(0, max).forEach((el, i) => play(el,
      [{ opacity: 0, transform: `translateY(${distance}px)` }, { opacity: 1, transform: "none" }],
      { duration, delay: i * each, fullOnly: true }));
  }

  /**
   * An animation before something goes away (a card closing, a toast leaving). The promise always settles, at the
   * latest shortly after `duration`, so callers can await it without risk; at "off" it settles at once.
   */
  function exit(el, keyframes, { duration = 110, easing = "cubic-bezier(.4,0,1,1)", fullOnly = false } = {}) {
    const a = play(el, keyframes, { duration, easing, fill: "forwards", fullOnly });
    if (!a) return Promise.resolve();
    return new Promise(done => {
      const t = setTimeout(done, time(duration) + 80);
      a.finished.then(() => { clearTimeout(t); done(); }, () => { clearTimeout(t); done(); });
    });
  }

  /** A number that counts up to `to` (formatted by `format`); plain text at "off" or for small numbers. */
  function countUp(el, to, { format = String, duration = 520 } = {}) {
    if (!el) return;
    const n = Number(to) || 0;
    if (!any() || n < 2 || typeof requestAnimationFrame !== "function") { el.textContent = format(n); return; }
    const start = performance.now(), dur = time(duration);
    const token = (el.__lamhaCount = (el.__lamhaCount || 0) + 1);
    const step = now => {
      if (el.__lamhaCount !== token) return; // a newer count took over
      const p = Math.min(1, (now - start) / dur);
      el.textContent = format(Math.round(n * (1 - Math.pow(1 - p, 3))));
      if (p < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  /**
   * Smooth re-ordering (FLIP): `mutate()` changes a list; items that moved glide from where they were.
   * Items are matched by `key(el)`; new items fade in. Full only, and at most `max` items move.
   */
  function flip(container, mutate, { key = el => el.dataset.id, duration = 200, max = 12 } = {}) {
    if (!full() || !container) { mutate(); return; }
    const before = new Map();
    for (const el of [...container.children].slice(0, 40)) { const k = key(el); if (k) before.set(k, el.getBoundingClientRect()); }
    mutate();
    let moved = 0;
    for (const el of container.children) {
      if (moved >= max) break;
      const k = key(el), was = k && before.get(k);
      const now = el.getBoundingClientRect();
      if (now.bottom < 0 || now.top > innerHeight) continue;
      if (!was) { play(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 160, fullOnly: true }); moved++; continue; }
      const dy = was.top - now.top;
      if (Math.abs(dy) < 1) continue;
      play(el, [{ transform: `translateY(${dy}px)` }, { transform: "none" }], { duration, fullOnly: true });
      moved++;
    }
  }

  /** Grows or shrinks `el` between two heights (e.g. a card whose content just changed). Full and subtle. */
  function resize(el, fromHeight, { duration = 200 } = {}) {
    if (!el || !any() || !fromHeight) return;
    const to = el.getBoundingClientRect().height;
    if (Math.abs(to - fromHeight) < 4) return;
    play(el, [{ height: fromHeight + "px", overflow: "hidden" }, { height: to + "px", overflow: "hidden" }], { duration, fill: "none" });
  }

  /**
   * An AI answer appearing word by word (full only), in at most ~250 ms whatever its length. The text is readable
   * and copyable at once; the spans are replaced by the plain text when the reveal is over. Long texts just fade.
   */
  function typeIn(el) {
    if (!el || !full()) return;
    const text = el.textContent;
    const words = text.split(/(\s+)/);
    if (words.length > 240 || el.children.length) { play(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 200 }); return; }
    const n = words.filter(w => w.trim()).length || 1;
    const each = Math.min(18, 250 / n);
    let i = 0;
    el.replaceChildren(...words.map(w => {
      if (!w.trim()) return document.createTextNode(w);
      const s = document.createElement("span");
      s.className = "lm-word";
      s.textContent = w;
      s.style.animationDelay = Math.round(i++ * each) + "ms";
      return s;
    }));
    setTimeout(() => { if (el.isConnected && el.querySelector(".lm-word")) el.textContent = text; }, Math.round(n * each) + 320);
  }

  /** Numbers the elements matching `selector` inside `el` (--i: 0, 1, 2 …, at most `max`) and marks it .lm-seq, so
   *  CSS can play them one after another (proofreading: each mistake struck through, then its fix). Full only. */
  function sequence(el, selector, { max = 16 } = {}) {
    if (!el || !full()) return;
    el.classList.add("lm-seq");
    [...el.querySelectorAll(selector)].forEach((x, i) => x.style.setProperty("--i", String(Math.min(i, max))));
  }

  /**
   * A small celebration: Arabic letters and dots bursting from `anchor` (an element), inside `layer` (default: body).
   * Full only; cleans up after itself. Decoration only: aria-hidden, pointer-events none.
   */
  function burst(anchor, { layer = document.body, count = 14, glyphs = ["ل", "م", "ح", "ة", "✦", "•"] } = {}) {
    if (!full() || !anchor || !layer || typeof anchor.animate !== "function") return;
    try { burstAt(anchor, layer, count, glyphs); } catch (_) { /* decoration only: never in the way */ }
  }
  function burstAt(anchor, layer, count, glyphs) {
    const r = anchor.getBoundingClientRect();
    const box = document.createElement("div");
    box.setAttribute("aria-hidden", "true");
    box.style.cssText = `position:fixed;left:${r.left + r.width / 2}px;top:${r.top + r.height / 2}px;width:0;height:0;pointer-events:none;z-index:2147483647;`;
    const colors = ["#4f46e5", "#06b6d4", "#16a34a", "#f59e0b", "#ec4899"];
    for (let i = 0; i < count; i++) {
      const s = document.createElement("span");
      s.textContent = glyphs[i % glyphs.length];
      s.style.cssText = `position:absolute;left:0;top:0;font:700 ${14 + (i % 3) * 4}px/1 system-ui,sans-serif;color:${colors[i % colors.length]};`;
      box.append(s);
      const angle = (Math.PI * 2 * i) / count + Math.random() * 0.4;
      const dist = 46 + Math.random() * 40;
      s.animate([
        { transform: "translate(-50%,-50%) scale(.4)", opacity: 1 },
        { transform: `translate(calc(-50% + ${Math.cos(angle) * dist}px), calc(-50% + ${Math.sin(angle) * dist - 18}px)) scale(1) rotate(${(Math.random() - 0.5) * 60}deg)`, opacity: 0 }
      ], { duration: 700 + Math.random() * 200, easing: "cubic-bezier(.1,.7,.3,1)", fill: "forwards" });
    }
    layer.append(box);
    setTimeout(() => box.remove(), 1000);
  }

  /* ---- the Lamha mark while waiting for an answer: what Lamha is doing, drawn in the mark ----
   * "book": a word, looked up in the dictionary (pages turn); "lens": a sentence, read line by line;
   * "write": the AI writing. The motion is CSS (content/styles.js for the card, motion.css for the pages). */
  const PAGE = "M12 7.5C9.6 6 6.6 6 4 6.6v10.6c2.6-.6 5.6-.6 8 .9z";
  const MARKS = {
    book: [["path", { class: "cover", d: "M12 6.5C9 4.5 5.5 4.5 2 5.5v13c3.5-1 7-1 10 1 3-2 6.5-2 10-1v-13c-3.5-1-7-1-10 1z" }], ["path", { d: "M12 6.5v13" }],
      ["path", { class: "pg", d: PAGE }], ["path", { class: "pg", d: PAGE }], ["path", { class: "pg", d: PAGE }]],
    lens: [["path", { class: "ln", d: "M3 7.5h16M3 12h18M3 16.5h12" }],
      ["g", { class: "glass" }, [["circle", { cx: "15", cy: "12", r: "4.4" }], ["path", { d: "M18.2 15.2 21.5 18.5" }]]]],
    write: [["path", { class: "w", d: "M4 10.5h16" }], ["path", { class: "w", d: "M4 15h16" }], ["path", { class: "w", d: "M8 19.5h12" }],
      ["path", { class: "spark", d: "M20 1.5l1 2.5 2.5 1-2.5 1-1 2.5-1-2.5-2.5-1 2.5-1z" }]]
  };
  function markSvg(kind) {
    const NS = "http://www.w3.org/2000/svg";
    const make = ([tag, attrs, kids = []]) => {
      const el = document.createElementNS(NS, tag);
      for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
      for (const kid of kids) el.append(make(kid));
      return el;
    };
    return make(["svg", { class: "wait " + kind, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", "stroke-width": "2.3",
      "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true" }, MARKS[kind]]);
  }
  /**
   * Shows the waiting mark in `host` (the card's .dot, the popup's .logo) and returns stop(). It appears only after
   * 250 ms, so an answer from the dictionary (a few ms) doesn't flicker; nothing at all at level "off".
   */
  function wait(host, kind) {
    if (!host || level === "off" || !Object.hasOwn(MARKS, kind)) return () => {};
    let mark = null;
    const timer = setTimeout(() => { mark = markSvg(kind); host.append(mark); host.classList.add("waiting"); }, 250);
    return () => { clearTimeout(timer); if (mark) mark.remove(); host.classList.remove("waiting"); };
  }

  return {
    SETTINGS, ready, attach, use, play, stagger, exit, countUp, flip, resize, burst, typeIn, sequence, time, wait,
    level: () => level, setting: () => setting, any, full,
    /** Called with the new level whenever it changes. */
    onChange: f => { listeners.add(f); return () => listeners.delete(f); }
  };
})();
