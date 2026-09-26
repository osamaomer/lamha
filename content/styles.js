/* Lamha — styles injected into the closed shadow roots (isolated from page CSS). */
// eslint-disable-next-line no-unused-vars
var LAMHA_CSS = `
:host { all: initial; }
* { box-sizing: border-box; }

.root {
  --font-ar: "SF Arabic", "Segoe UI", "Noto Sans Arabic", "Noto Naskh Arabic", "Geeza Pro", Tahoma, sans-serif;
  --font-ui: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", "Noto Sans Arabic", Roboto, Tahoma, sans-serif;
  --font-head: "New York", "Iowan Old Style", "Palatino Linotype", Georgia, serif;

  --bg: #fcfcfd;
  --bg-solid: #fcfcfd;
  --fg: #1d1d1f;
  --muted: #6e6e73;
  --faint: #8e8e93;
  --line: rgba(0, 0, 0, 0.08);
  --hover: rgba(0, 0, 0, 0.05);
  --accent: #4f46e5;
  --accent-soft: rgba(79, 70, 229, 0.09);
  --accent-fg: #ffffff;
  --chip: rgba(0, 0, 0, 0.045);
  --shadow: 0 18px 50px -12px rgba(0, 0, 0, 0.28), 0 4px 14px rgba(0, 0, 0, 0.08), 0 0 0 0.5px rgba(0, 0, 0, 0.12);
  --pill-bg: rgba(28, 28, 30, 0.92);
  --pill-fg: #ffffff;
  --skeleton: rgba(0, 0, 0, 0.06);

  font-family: var(--font-ui);
  font-size: 14px;
  line-height: 1.5;
  color: var(--fg);
  -webkit-font-smoothing: antialiased;
}
.root.dark {
  --bg: #242426;
  --bg-solid: #242426;
  --fg: #f5f5f7;
  --muted: #a1a1a6;
  --faint: #8e8e93;
  --line: rgba(255, 255, 255, 0.1);
  --hover: rgba(255, 255, 255, 0.07);
  --accent: #8b93ff;
  --accent-soft: rgba(139, 147, 255, 0.14);
  --accent-fg: #0b0b12;
  --chip: rgba(255, 255, 255, 0.07);
  --shadow: 0 18px 50px -12px rgba(0, 0, 0, 0.6), 0 4px 14px rgba(0, 0, 0, 0.3), 0 0 0 0.5px rgba(255, 255, 255, 0.14);
  --pill-bg: rgba(242, 242, 247, 0.95);
  --pill-fg: #1c1c1e;
  --skeleton: rgba(255, 255, 255, 0.08);
}

button { font: inherit; color: inherit; border: 0; background: none; cursor: pointer; padding: 0; margin: 0; }
button:focus-visible, a:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: 6px; }
svg { display: block; flex: none; }
[dir="rtl"], .ar { font-family: var(--font-ar); }

/* ---------------- trigger pill ---------------- */
.pill {
  position: fixed;
  display: flex; align-items: center;
  height: 32px; padding: 0 3px;
  border-radius: 999px;
  background: var(--pill-bg);
  color: var(--pill-fg);
  box-shadow: 0 6px 20px rgba(0,0,0,.22), 0 0 0 .5px rgba(0,0,0,.2);
  backdrop-filter: blur(16px) saturate(180%);
  font-family: var(--font-ar);
  font-size: 13px; font-weight: 600; letter-spacing: 0;
  white-space: nowrap;
  user-select: none;
  transform-origin: 50% 100%;
  animation: pill-in .16s cubic-bezier(.2,.9,.3,1.3) both;
  pointer-events: auto;
}
.pill.below { transform-origin: 50% 0%; }
.pill-btn {
  display: flex; align-items: center; gap: 6px;
  height: 26px; padding: 0 9px 0 8px; border-radius: 999px;
  transition: background .12s;
}
.pill-btn:hover { background: rgba(255, 255, 255, .14); }
.root.dark .pill-btn:hover { background: rgba(0, 0, 0, .08); }
.pill-btn:active { transform: scale(.96); }
.pill-sep { width: 1px; height: 16px; background: currentColor; opacity: .25; }
.pill .kbd { opacity: .55; font-family: var(--font-ui); font-weight: 500; font-size: 11px; margin-inline-start: 2px; }
.pill::after {
  content: ""; position: absolute; left: var(--arrow-x, 50%); bottom: -5px;
  width: 10px; height: 10px; margin-left: -5px;
  background: var(--pill-bg); transform: rotate(45deg); border-radius: 2px;
  z-index: -1;
}
.pill.below::after { bottom: auto; top: -5px; }
@keyframes pill-in { from { opacity: 0; transform: scale(.7) translateY(4px); } to { opacity: 1; transform: none; } }

/* ---------------- card ---------------- */
.card {
  position: fixed;
  width: 384px; max-width: calc(100vw - 16px);
  display: flex; flex-direction: column;
  background: var(--bg);
  border-radius: 14px;
  box-shadow: var(--shadow);
  overflow: hidden;
  pointer-events: auto;
  animation: card-in .18s cubic-bezier(.2,.8,.2,1) both;
  text-align: start;
}
.card.above { animation-name: card-in-up; }
.card:focus { outline: none; } /* focused programmatically so Esc and keys work; its buttons keep their own focus ring */
@keyframes card-in { from { opacity: 0; transform: translateY(-6px) scale(.98); } to { opacity: 1; transform: none; } }
@keyframes card-in-up { from { opacity: 0; transform: translateY(6px) scale(.98); } to { opacity: 1; transform: none; } }

.bar {
  display: flex; align-items: center; gap: 4px;
  padding: 8px 8px 8px 12px;
  border-bottom: 1px solid var(--line);
  direction: rtl;
}
.brand { display: flex; align-items: center; gap: 6px; font-family: var(--font-ar); font-size: 12px; font-weight: 700; color: var(--muted); }
.brand .dot { width: 18px; height: 18px; border-radius: 5px; background: linear-gradient(135deg, #6366f1, #06b6d4); display: grid; place-items: center; color: #fff; }
.lang { font-size: 11.5px; color: var(--faint); font-family: var(--font-ar); margin-inline-start: 4px; }
.spacer { flex: 1; }
.badge {
  display: inline-flex; align-items: center; gap: 4px;
  font-family: var(--font-ar); font-size: 10.5px; font-weight: 700;
  color: #0f8a5f; background: rgba(16, 185, 129, .12);
  padding: 2px 7px; border-radius: 999px; margin-inline-start: 6px;
}
.root.dark .badge { color: #5ee0a8; background: rgba(16, 185, 129, .16); }
.icon-btn {
  width: 28px; height: 28px; border-radius: 7px;
  display: grid; place-items: center;
  color: var(--muted);
  transition: background .12s, color .12s;
}
.icon-btn:hover { background: var(--hover); color: var(--fg); }
.icon-btn.on { color: var(--accent); }
.icon-btn.mark { width: 34px; height: 34px; border-radius: 50%; }
.icon-btn.mark.on svg { fill: currentColor; }
.icon-btn[disabled] { opacity: .35; pointer-events: none; }

.body { overflow-y: auto; overscroll-behavior: contain; padding: 14px 16px 16px; scrollbar-width: thin; }

/* headword */
.head { direction: ltr; display: flex; align-items: flex-start; gap: 10px; }
.head .w { flex: 1; min-width: 0; }
.headword { font-family: var(--font-head); font-size: 26px; line-height: 1.15; font-weight: 700; letter-spacing: -.01em; word-break: break-word; }
.headword.ar { font-family: var(--font-ar); font-weight: 700; }
.phon { color: var(--muted); font-size: 13.5px; margin-top: 3px; font-family: var(--font-ui); }
.speak {
  width: 34px; height: 34px; border-radius: 50%;
  display: grid; place-items: center;
  background: var(--accent-soft); color: var(--accent);
  transition: transform .12s, background .12s;
}
.speak:hover { transform: scale(1.06); }
.speak.playing { animation: pulse 1s ease-in-out infinite; }
@keyframes pulse { 50% { box-shadow: 0 0 0 6px var(--accent-soft); } }

.form-of { font-family: var(--font-ar); font-size: 12px; color: var(--muted); margin-top: 4px; direction: rtl; text-align: left; }
.form-of b { color: var(--fg); font-family: var(--font-ui); }
.form-of .link { color: var(--accent); font-weight: 700; font-family: var(--font-ui); }
.form-of .link:hover { text-decoration: underline; }
.spell { direction: rtl; font-family: var(--font-ar); font-size: 12.5px; color: var(--muted); margin-top: 8px; }
.spell button { color: var(--accent); font-weight: 600; direction: ltr; unicode-bidi: isolate; }

/* hero translation */
.hero {
  margin-top: 12px;
  padding: 12px 14px;
  border-radius: 11px;
  background: var(--accent-soft);
  display: flex; align-items: center; gap: 10px;
}
.hero .t { flex: 1; min-width: 0; font-size: 22px; font-weight: 700; line-height: 1.35; color: var(--fg); word-break: break-word; }
.hero .t.none { font-size: 13.5px; font-weight: 500; color: var(--muted); line-height: 1.6; }
.hero .t.none .gloss { display: block; margin-top: 4px; color: var(--fg); font-family: var(--font-ui); font-size: 13px; text-align: left; }
.ctx-label { display: inline-block; font-family: var(--font-ar); font-size: 11px; font-weight: 700; color: var(--accent); margin-bottom: 2px; }
.hero .ctx {
  margin-top: 8px; padding-top: 8px; border-top: 1px dashed var(--line);
  font-family: var(--font-ar); font-size: 13px; line-height: 1.75; color: var(--muted);
  display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden;
}
.hero .ctx mark { background: transparent; color: var(--fg); font-weight: 700; box-shadow: inset 0 -0.45em 0 var(--accent-soft); }
.hero .t.long { font-size: 16.5px; font-weight: 500; line-height: 1.75; }
.hero .t[dir="ltr"] { font-family: var(--font-ui); }
.hero .tr { font-size: 12px; color: var(--muted); margin-top: 2px; font-weight: 400; font-family: var(--font-ui); direction: ltr; text-align: right; }
.hero .actions { display: flex; flex-direction: column; gap: 2px; align-self: flex-start; }

.source {
  direction: ltr; text-align: left;
  font-size: 13px; color: var(--muted);
  line-height: 1.55;
  display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden;
  cursor: pointer;
}
.source.open { -webkit-line-clamp: unset; }
.src-row { display: flex; align-items: flex-start; gap: 6px; }
.src-row .source { flex: 1; min-width: 0; }
.icon-btn.playing { color: var(--accent); animation: pulse 1s ease-in-out infinite; }
.source[dir="rtl"] { text-align: right; }

/* sections */
.sec { margin-top: 18px; }
.sec-h {
  display: flex; align-items: center; gap: 8px;
  font-family: var(--font-ar); font-size: 11.5px; font-weight: 700;
  color: var(--faint); letter-spacing: .02em;
  margin-bottom: 8px; direction: rtl;
}
.sec-h::after { content: ""; flex: 1; height: 1px; background: var(--line); }

.pos-row { direction: rtl; margin-bottom: 10px; }
.pos { display: inline-block; font-family: var(--font-ar); font-size: 12px; font-weight: 700; color: var(--accent); margin-inline-end: 6px; }
.chips { display: inline-flex; flex-wrap: wrap; gap: 5px; vertical-align: middle; }
.chip {
  font-family: var(--font-ar); font-size: 13.5px;
  padding: 3px 10px; border-radius: 999px;
  background: var(--chip); color: var(--fg);
  transition: background .12s;
}
.chip:hover { background: var(--accent-soft); color: var(--accent); }
.chip.en { font-family: var(--font-ui); font-size: 12.5px; direction: ltr; }
.chip.more { color: var(--muted); font-size: 12px; }
div.pos { display: block; margin-bottom: 6px; }
.sense { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; margin-bottom: 8px; }
.sense .hint { font-family: var(--font-ui); font-size: 11.5px; color: var(--faint); direction: ltr; }

.defs { list-style: none; margin: 0; padding: 0; counter-reset: d; }
.def { position: relative; padding-inline-start: 26px; margin-bottom: 12px; direction: rtl; counter-increment: d; }
.def::before {
  content: counter(d); position: absolute; inset-inline-start: 0; top: 2px;
  width: 18px; height: 18px; border-radius: 50%;
  background: var(--chip); color: var(--muted);
  font-size: 11px; font-weight: 700; display: grid; place-items: center; font-family: var(--font-ui);
}
.def.best { background: var(--accent-soft); border-radius: 10px; padding: 8px 10px 8px 8px; padding-inline-start: 34px; margin-inline: -8px; }
.def.best::before { inset-inline-start: 8px; top: 10px; background: var(--accent); color: var(--accent-fg); }
.sense.best .chip { background: var(--accent-soft); color: var(--accent); font-weight: 700; }
.def .ar-w { font-family: var(--font-ar); font-size: 15px; font-weight: 700; color: var(--accent); line-height: 1.6; }
.def .ar-g { font-family: var(--font-ar); font-size: 14.5px; line-height: 1.7; }
.def .ar-g.sk-line { height: 14px; width: 70%; margin: 4px 0 6px; border-radius: 6px; background: var(--skeleton); }
.def .en-g { direction: ltr; text-align: left; font-size: 13px; color: var(--muted); margin-top: 2px; }
.def .ex { direction: ltr; text-align: left; font-size: 13px; color: var(--muted); font-style: italic; margin-top: 3px; padding-left: 9px; border-left: 2px solid var(--line); }
.def .syn { direction: ltr; margin-top: 6px; display: flex; flex-wrap: wrap; gap: 4px; }
.def .syn .chip { font-size: 12px; padding: 1px 8px; }

.examples { direction: ltr; margin: 0; padding: 0 0 0 16px; color: var(--muted); font-size: 13px; }
.examples li { margin-bottom: 4px; }

/* wiki */
.wiki { display: flex; gap: 12px; align-items: flex-start; }
.wiki img { width: 64px; height: 64px; border-radius: 9px; object-fit: cover; flex: none; background: var(--chip); }
.wiki .x { font-size: 13.5px; line-height: 1.7; display: -webkit-box; -webkit-line-clamp: 5; -webkit-box-orient: vertical; overflow: hidden; }
.wiki .x[dir="ltr"] { font-size: 13px; line-height: 1.55; }
.wiki a { color: var(--accent); font-size: 12px; font-weight: 600; text-decoration: none; display: inline-flex; gap: 4px; align-items: center; margin-top: 4px; font-family: var(--font-ar); }
.wiki a:hover { text-decoration: underline; }

/* footer */
.foot {
  display: flex; align-items: center; gap: 4px;
  padding: 6px 8px 6px 12px;
  border-top: 1px solid var(--line);
  direction: rtl;
  font-family: var(--font-ar); font-size: 12px; color: var(--muted);
}
.foot a { color: var(--muted); text-decoration: none; display: inline-flex; gap: 5px; align-items: center; padding: 4px 8px; border-radius: 7px; }
.foot a:hover { background: var(--hover); color: var(--fg); }

/* states */
.sk { background: var(--skeleton); border-radius: 7px; position: relative; overflow: hidden; }
.sk::after {
  content: ""; position: absolute; inset: 0;
  background: linear-gradient(90deg, transparent, rgba(255,255,255,.35), transparent);
  animation: shimmer 1.1s infinite;
}
.root.dark .sk::after { background: linear-gradient(90deg, transparent, rgba(255,255,255,.08), transparent); }
@keyframes shimmer { from { transform: translateX(-100%); } to { transform: translateX(100%); } }

.err { direction: rtl; font-family: var(--font-ar); text-align: center; padding: 18px 8px; color: var(--muted); }
.err b { display: block; color: var(--fg); font-size: 15px; margin-bottom: 4px; }
.btn {
  display: inline-flex; align-items: center; gap: 6px;
  padding: 7px 14px; border-radius: 9px;
  background: var(--accent); color: var(--accent-fg);
  font-family: var(--font-ar); font-weight: 600; font-size: 13px;
  margin-top: 12px;
}
.btn:hover { filter: brightness(1.08); }

.btn.ghost { background: var(--chip); color: var(--fg); }
.btn.ghost:hover { background: var(--hover); filter: none; }

/* ---------------- writing tools ---------------- */
.card.write { width: 440px; }
.w-src { -webkit-line-clamp: 2; }
.tools { display: flex; flex-wrap: wrap; gap: 6px; direction: rtl; margin-top: 12px; }
.chip.on, .chip.on:hover { background: var(--accent); color: var(--accent-fg); }
.w-out { margin-top: 14px; }
.w-out:empty { display: none; }
.w-text {
  white-space: pre-wrap; word-break: break-word;
  font-size: 14.5px; line-height: 1.65;
  padding: 12px 14px; border-radius: 11px;
  background: var(--accent-soft);
  text-align: start;
}
.w-text[dir="ltr"] { font-family: var(--font-ui); }
.w-text ins { text-decoration: none; background: rgba(16, 185, 129, .2); border-radius: 3px; }
.w-text del { color: #dc2626; opacity: .8; }
.w-text del + ins { margin-left: .3em; }
.root.dark .w-text del { color: #f87171; }
.w-actions { display: flex; align-items: center; gap: 6px; direction: rtl; margin-top: 10px; }
.w-actions .btn { margin-top: 0; padding: 6px 12px; }
.w-ok {
  display: flex; align-items: center; gap: 8px; direction: rtl;
  font-family: var(--font-ar); font-weight: 600;
  color: #0f8a5f; background: rgba(16, 185, 129, .12);
  padding: 12px 14px; border-radius: 11px;
}
.root.dark .w-ok { color: #5ee0a8; background: rgba(16, 185, 129, .16); }
.issues { list-style: none; margin: 0; padding: 0; }
.issues li { padding: 8px 0; border-top: 1px solid var(--line); }
.issues li:first-child { border-top: 0; padding-top: 0; }
.issues .fix { direction: ltr; text-align: left; font-size: 13.5px; }
.issues del { color: #dc2626; }
.issues ins { text-decoration: none; color: #0f8a5f; font-weight: 600; }
.root.dark .issues del { color: #f87171; }
.root.dark .issues ins { color: #5ee0a8; }
.issues .cat {
  display: inline-block; margin-top: 4px;
  font-family: var(--font-ar); font-size: 11px; font-weight: 700;
  color: var(--accent); background: var(--accent-soft);
  padding: 1px 8px; border-radius: 999px;
}
.issues li { direction: rtl; }
.issues .why { font-family: var(--font-ar); font-size: 13px; line-height: 1.6; color: var(--muted); margin-top: 2px; }
.w-input {
  display: block; width: 100%; min-height: 64px; resize: vertical;
  font-family: var(--font-ar); font-size: 14px; line-height: 1.6; color: var(--fg);
  background: var(--chip); border: 0; border-radius: 10px; padding: 10px 12px;
}
.w-input:focus { outline: 2px solid var(--accent); }
.w-note { direction: rtl; font-family: var(--font-ar); font-size: 11.5px; font-weight: 700; color: var(--faint); margin-top: 10px; }
.w-note + .tools { margin-top: 6px; }

.toast {
  position: fixed; left: 50%; bottom: 28px; transform: translateX(-50%);
  background: var(--pill-bg); color: var(--pill-fg);
  padding: 8px 16px; border-radius: 999px;
  font-family: var(--font-ar); font-size: 13px; font-weight: 600;
  box-shadow: 0 8px 24px rgba(0,0,0,.25);
  animation: pill-in .18s ease both;
  pointer-events: none;
}

/* ---------------- page-translation bar ---------------- */
.pbar {
  position: fixed; top: 12px; left: 50%; transform: translateX(-50%);
  display: flex; align-items: center; gap: 4px;
  padding: 5px 5px 5px 12px;
  border-radius: 999px;
  background: var(--bg);
  box-shadow: var(--shadow);
  direction: rtl;
  font-family: var(--font-ar); font-size: 13px;
  pointer-events: auto;
  animation: pbar-in .25s cubic-bezier(.2,.8,.2,1) both;
  transition: opacity .2s, transform .2s;
}
.pbar.mini { opacity: .5; }
.pbar:hover { opacity: 1; }
@keyframes pbar-in { from { opacity: 0; transform: translate(-50%, -16px); } to { opacity: 1; transform: translate(-50%, 0); } }
.pbar .status { display: flex; align-items: center; gap: 8px; padding-inline: 4px 8px; font-weight: 600; white-space: nowrap; }
.pbar .spin { width: 14px; height: 14px; border-radius: 50%; border: 2px solid var(--accent-soft); border-top-color: var(--accent); animation: spin .7s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
.pbar .ok { color: #16a34a; }
.pbar .seg { display: flex; background: var(--chip); border-radius: 999px; padding: 2px; }
.pbar .seg button { padding: 4px 12px; border-radius: 999px; font-size: 12.5px; font-weight: 600; color: var(--muted); font-family: var(--font-ar); }
.pbar .seg button.on { background: var(--bg-solid); color: var(--fg); box-shadow: 0 1px 3px rgba(0,0,0,.12); }

/* English interface: Lamha's own labels run left to right (Arabic meanings and definitions keep their direction) */
.root.en .bar, .root.en .foot, .root.en .sec-h, .root.en .spell, .root.en .form-of, .root.en .err, .root.en .tools,
.root.en .w-actions, .root.en .w-ok, .root.en .issues li, .root.en .w-note, .root.en .pill, .root.en .pbar {
  direction: ltr; font-family: var(--font-ui);
}

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation-duration: .001ms !important; animation-iteration-count: 1 !important; transition-duration: .001ms !important; }
}
`;
