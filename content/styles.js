/* Lamha — styles injected into the closed shadow roots (isolated from page CSS). */
// eslint-disable-next-line no-unused-vars
var LAMHA_CSS = `
:host { all: initial; }
* { box-sizing: border-box; }

.root {
  --font-ar: "SF Arabic", "Segoe UI", "Noto Sans Arabic", "Noto Naskh Arabic", "Geeza Pro", Tahoma, sans-serif;
  --font-ui: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", "Noto Sans Arabic", Roboto, Tahoma, sans-serif;
  --font-head: "New York", "Iowan Old Style", "Palatino Linotype", Georgia, serif;

  /* the same colours as the pages (shared/ui.css; --bg here is their --surface): tools/test-ui.mjs checks they match */
  --bg: #ffffff;
  --bg-solid: #ffffff;
  --fg: #1d1d1f;
  --muted: #5f5f64; /* 4.5:1 also on the tinted meaning box */
  --faint: #707075;
  --line: rgba(0, 0, 0, 0.08);
  --hover: rgba(0, 0, 0, 0.05);
  --accent: #4f46e5;
  --accent-soft: rgba(79, 70, 229, 0.09);
  --accent-fg: #ffffff;
  --btn: #4f46e5;
  --btn-fg: #ffffff;
  --ok-soft: rgba(22, 163, 74, 0.12);
  --danger-soft: rgba(220, 38, 38, 0.1);
  --toast-bg: #1c1c1e;
  --toast-fg: #ffffff;
  --toast-act: #a5b4fc;
  --celebrate: linear-gradient(135deg, rgba(79, 70, 229, 0.11), rgba(6, 182, 212, 0.11));
  --chip: rgba(0, 0, 0, 0.045);
  --shadow: 0 18px 50px -12px rgba(0, 0, 0, 0.28), 0 4px 14px rgba(0, 0, 0, 0.08), 0 0 0 0.5px rgba(0, 0, 0, 0.12);
  --pill-bg: rgba(28, 28, 30, 0.92);
  --pill-fg: #ffffff;
  --skeleton: rgba(0, 0, 0, 0.06);
  --ok: #166534;
  --danger: #c81e1e;
  --scroll: rgba(0, 0, 0, 0.22);
  --r-sm: 8px;
  --r-md: 10px;
  --r-lg: 14px;

  /* scrollbars and form controls in the card's own theme: the shadow root would take the web page's (usually light) */
  color-scheme: light;
  scrollbar-color: var(--scroll) transparent;
  font-family: var(--font-ui);
  font-size: 14px;
  line-height: 1.5;
  color: var(--fg);
  -webkit-font-smoothing: antialiased;
  direction: rtl; /* the interface's own direction: \`all: initial\` doesn't reset it, so it would follow the web page */
}
.root.en { direction: ltr; }
.root.dark {
  --bg: #2c2c2e;
  --bg-solid: #2c2c2e;
  --fg: #f5f5f7;
  --muted: #a1a1a6;
  --faint: #98989d;
  --line: rgba(255, 255, 255, 0.1);
  --hover: rgba(255, 255, 255, 0.07);
  --accent: #8b93ff;
  --accent-soft: rgba(139, 147, 255, 0.15);
  --accent-fg: #0b0b12;
  --btn: #5e5ce6;
  --btn-fg: #ffffff;
  --ok-soft: rgba(74, 222, 128, 0.13);
  --danger-soft: rgba(248, 113, 113, 0.14);
  --toast-bg: rgba(242, 242, 247, 0.96);
  --toast-fg: #1c1c1e;
  --toast-act: #4f46e5;
  --celebrate: linear-gradient(135deg, rgba(139, 147, 255, 0.22), rgba(34, 211, 238, 0.13));
  --chip: rgba(255, 255, 255, 0.07);
  --shadow: 0 18px 50px -12px rgba(0, 0, 0, 0.6), 0 4px 14px rgba(0, 0, 0, 0.3), 0 0 0 0.5px rgba(255, 255, 255, 0.14);
  --pill-bg: rgba(242, 242, 247, 0.95);
  --pill-fg: #1c1c1e;
  --skeleton: rgba(255, 255, 255, 0.08);
  --ok: #4ade80;
  --danger: #f87171;
  --scroll: rgba(255, 255, 255, 0.2);
  color-scheme: dark;
}

button { font: inherit; color: inherit; border: 0; background: none; cursor: pointer; padding: 0; margin: 0; }
button:focus-visible, a:focus-visible, [role="button"]:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: 6px; }
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
/* the first double-click Write button explains itself once, on the side away from the text box */
.pill-tip {
  position: absolute; bottom: calc(100% + 8px); left: 0;
  width: max-content; max-width: 250px; padding: 7px 11px; border-radius: var(--r-md);
  background: var(--pill-bg); color: var(--pill-fg); box-shadow: 0 6px 20px rgba(0,0,0,.22);
  font-size: 12px; font-weight: 500; line-height: 1.5; white-space: normal;
  animation: pill-in .2s .15s cubic-bezier(.2,.9,.3,1.3) both;
}
.pill.below .pill-tip { bottom: auto; top: calc(100% + 8px); }
.root.en .pill-tip { font-family: var(--font-ui); }
@keyframes pill-in { from { opacity: 0; transform: scale(.7) translateY(4px); } to { opacity: 1; transform: none; } }

/* ---------------- card ---------------- */
.card {
  position: fixed;
  width: 384px; max-width: calc(100vw - 16px);
  display: flex; flex-direction: column;
  background: var(--bg);
  border-radius: var(--r-lg);
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
/* العربية ⇄ English on word cards: the meaning translated, or an English–English dictionary */
.dsw { display: flex; background: var(--chip); border-radius: 999px; padding: 2px; margin-inline-start: 6px; }
.dsw button { padding: 2px 9px; border-radius: 999px; font-family: var(--font-ar); font-size: 11px; font-weight: 600; color: var(--muted); }
.dsw button.on { background: var(--bg-solid); color: var(--fg); box-shadow: 0 1px 3px rgba(0,0,0,.12); }
.badge {
  display: inline-flex; align-items: center; gap: 4px;
  font-family: var(--font-ar); font-size: 10.5px; font-weight: 700;
  color: var(--ok); background: var(--ok-soft);
  padding: 2px 7px; border-radius: 999px; margin-inline-start: 6px;
}
.badge.icon { padding: 4px; margin-inline-start: 4px; cursor: help; } /* the name is in its tooltip */
.badge.ai { color: var(--accent); background: var(--accent-soft); }
.bar > * { white-space: nowrap; flex-shrink: 0; } /* one line, whatever the interface language */
.bar > .spacer { flex-shrink: 1; }
.icon-btn {
  width: 28px; height: 28px; border-radius: var(--r-sm);
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
/* speaking: the sound waves pulse until it's done */
.playing svg path:nth-child(n+2) { animation: lm-wave .9s ease-in-out infinite; }
.playing svg path:nth-child(3) { animation-delay: .22s; }
@keyframes lm-wave { 0%, 100% { opacity: .25; } 50% { opacity: 1; } }

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
  border-radius: var(--r-md);
  background: var(--accent-soft);
  display: flex; align-items: center; gap: 10px;
}
.hero .t { flex: 1; min-width: 0; font-size: 22px; font-weight: 700; line-height: 1.35; color: var(--fg); word-break: break-word; white-space: pre-line; }
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
.hero .roots { font-family: var(--font-ar); font-size: 12.5px; color: var(--muted); margin-top: 6px; } /* Arabic explained in Arabic: root · plural */
.hero .actions { display: flex; flex-direction: column; gap: 2px; align-self: flex-start; }

.source {
  direction: ltr; text-align: left;
  font-size: 13px; color: var(--muted);
  line-height: 1.55; white-space: pre-line;
  display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden;
  cursor: pointer;
}
.source.open { -webkit-line-clamp: unset; }
.src-row { display: flex; align-items: flex-start; gap: 6px; }
.src-row .source { flex: 1; min-width: 0; }
.icon-btn.playing { color: var(--accent); }
.icon-btn.copied { color: var(--ok); } /* copy → ✓ for a moment */
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
.wiki-offline {
  display: inline-flex; align-items: center; gap: 4px; padding: 1px 7px; border-radius: 999px;
  background: var(--chip); color: var(--muted); font-size: 11px; font-weight: 600; letter-spacing: 0;
}

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
.def.best { background: var(--accent-soft); border-radius: var(--r-md); padding: 8px 10px 8px 8px; padding-inline-start: 34px; margin-inline: -8px; }
.def.best::before { inset-inline-start: 8px; top: 10px; background: var(--btn); color: var(--btn-fg); }
.sense.best .chip { background: var(--accent-soft); color: var(--accent); font-weight: 700; }
.def .ar-w { font-family: var(--font-ar); font-size: 15px; font-weight: 700; color: var(--accent); line-height: 1.6; }
.def .ar-g { font-family: var(--font-ar); font-size: 14.5px; line-height: 1.7; }
.def .ar-g.sk-line { height: 14px; width: 70%; margin: 4px 0 6px; border-radius: 6px; background: var(--skeleton); }
.def .en-g { direction: ltr; text-align: left; font-size: 13px; color: var(--muted); margin-top: 2px; }
.def .ex { direction: ltr; text-align: left; font-size: 13px; color: var(--muted); font-style: italic; margin-top: 3px; padding-left: 9px; border-left: 2px solid var(--line); }
.def .syn { direction: ltr; margin-top: 6px; display: flex; flex-wrap: wrap; gap: 4px; }
.def .syn .chip { font-size: 12px; padding: 1px 8px; }
/* a word explained in a right-to-left language (Arabic in Arabic): its examples and synonyms read from the right */
.def .ex[dir="rtl"] { direction: rtl; text-align: right; font-family: var(--font-ar); font-style: normal; padding-left: 0; border-left: 0; padding-right: 9px; border-right: 2px solid var(--line); }
.def .syn[dir="rtl"] { direction: rtl; }

.examples { direction: ltr; margin: 0; padding: 0 0 0 16px; color: var(--muted); font-size: 13px; }
.examples li { margin-bottom: 4px; }

/* wiki */
.wiki { display: flex; gap: 12px; align-items: flex-start; }
.wiki img { width: 64px; height: 64px; border-radius: var(--r-md); object-fit: cover; flex: none; background: var(--chip); }
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
.foot a { color: var(--muted); text-decoration: none; display: inline-flex; gap: 5px; align-items: center; padding: 4px 8px; border-radius: var(--r-sm); }
.foot a:hover { background: var(--hover); color: var(--fg); }

/* states */
.sk { background: var(--skeleton); border-radius: var(--r-sm); position: relative; overflow: hidden; }
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
  padding: 7px 14px; border-radius: var(--r-md);
  background: var(--btn); color: var(--btn-fg);
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
.chip.on, .chip.on:hover { background: var(--btn); color: var(--btn-fg); }
.chip.on:focus-visible { outline-color: color-mix(in srgb, var(--btn) 45%, transparent); outline-width: 3px; outline-offset: 1px; } /* already lit: a soft halo, not a second ring */
.chip[disabled] { opacity: .45; cursor: default; pointer-events: none; } /* writing tools before an AI is set up */
.chip.tool .num { font-family: var(--font-ui); font-size: 10.5px; font-weight: 700; opacity: .65; margin-inline-end: 5px; } /* press it to run the tool */
.w-out > .btn .kbd { font-family: var(--font-ui); font-size: 11px; font-weight: 500; opacity: .7; margin-inline-start: 4px; }
.w-out { margin-top: 14px; }
.w-out:empty { display: none; }
.w-text {
  white-space: pre-wrap; word-break: break-word;
  font-size: 14.5px; line-height: 1.65;
  padding: 12px 14px; border-radius: var(--r-md);
  background: var(--accent-soft);
  text-align: start;
}
.w-text[dir="ltr"] { font-family: var(--font-ui); }
.w-text ins { text-decoration: none; color: var(--ok); background: var(--ok-soft); border-radius: 3px; font-weight: 600; }
.w-text del { color: var(--danger); }
.w-text del + ins { margin-left: .3em; }
.w-actions { display: flex; align-items: center; gap: 6px; direction: rtl; margin-top: 10px; }
.w-actions .btn { margin-top: 0; padding: 6px 12px; }
.w-ok {
  display: flex; align-items: center; gap: 8px; direction: rtl;
  font-family: var(--font-ar); font-weight: 600;
  color: var(--ok); background: var(--ok-soft);
  padding: 12px 14px; border-radius: var(--r-md);
}
.issues { list-style: none; margin: 0; padding: 0; }
.issues li { padding: 8px 0; border-top: 1px solid var(--line); }
.issues li:first-child { border-top: 0; padding-top: 0; }
.issues .fix { direction: ltr; text-align: left; font-size: 13.5px; }
.issues del { color: var(--danger); }
.issues ins { text-decoration: none; color: var(--ok); background: var(--ok-soft); border-radius: 3px; font-weight: 600; }
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
  background: var(--chip); border: 0; border-radius: var(--r-md); padding: 10px 12px;
}
.w-input:focus { outline: 2px solid var(--accent); }
.w-input:placeholder-shown { direction: inherit; } /* an empty box follows the interface (dir="auto" would make it left to right) */
.w-note { direction: rtl; font-family: var(--font-ar); font-size: 11.5px; font-weight: 700; color: var(--faint); margin-top: 10px; }
.w-note + .tools { margin-top: 6px; }

.toast {
  position: fixed; left: 50%; bottom: 28px; transform: translateX(-50%);
  background: var(--toast-bg); color: var(--toast-fg);
  padding: 8px 16px; border-radius: 999px;
  font-family: var(--font-ar); font-size: 13px; font-weight: 600;
  box-shadow: 0 8px 24px rgba(0,0,0,.25);
  animation: pill-in .18s ease both;
  pointer-events: none;
}

/* ---------------- page-translation bar ---------------- */
.pbar { /* bottom centre: the top of a page is where sites keep their navigation and search */
  position: fixed; bottom: 16px; left: 50%; transform: translateX(-50%);
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
.pbar.mini .status { display: none; } /* idle: just the switch and ✕, still fully readable */
.pbar.mini:hover .status, .pbar.mini:focus-within .status { display: flex; }
@keyframes pbar-in { from { opacity: 0; transform: translate(-50%, 16px); } to { opacity: 1; transform: translate(-50%, 0); } }
.pbar .status { display: flex; align-items: center; gap: 8px; padding-inline: 4px 8px; font-weight: 600; white-space: nowrap; }
.pbar .spin { width: 14px; height: 14px; border-radius: 50%; border: 2px solid var(--accent-soft); border-top-color: var(--accent); animation: spin .7s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
.pbar .ok { color: var(--ok); }
.pbar .warn { color: var(--danger); }
.pbar .seg { display: flex; background: var(--chip); border-radius: 999px; padding: 2px; }
.pbar .seg button { padding: 4px 12px; border-radius: 999px; font-size: 12.5px; font-weight: 600; color: var(--muted); font-family: var(--font-ar); }
.pbar .seg button.on { background: var(--bg-solid); color: var(--fg); box-shadow: 0 1px 3px rgba(0,0,0,.12); }

/* English interface: Lamha's own labels run left to right (Arabic meanings and definitions keep their direction) */
.root.en .bar, .root.en .foot, .root.en .sec-h, .root.en .spell, .root.en .form-of, .root.en .err, .root.en .tools,
.root.en .w-actions, .root.en .w-ok, .root.en .issues li, .root.en .w-note, .root.en .pill, .root.en .pbar {
  direction: ltr; font-family: var(--font-ui);
}
/* …and meanings and definitions line up under their English headings: labels and numbers on the left, Arabic text
   keeps its right-to-left shaping but starts at the same edge */
.root.en .pos-row, .root.en .def { direction: ltr; }
.root.en .pos { font-family: var(--font-ui); }
.root.en .def [dir="rtl"] { text-align: left; }
/* "Read more on Wikipedia" starts at the left edge like the rest of the card (the icon stays before the text) */
.root.en .wiki a { display: flex; width: fit-content; margin-right: auto; font-family: var(--font-ui); }

/* a milestone ("word number 100") at the top of the card */
.milestone {
  margin: -2px 0 10px; padding: 7px 12px; border-radius: var(--r-md);
  background: var(--celebrate); font-family: var(--font-ar); font-size: 12.5px; font-weight: 600; direction: rtl;
}
.root.en .milestone { direction: ltr; font-family: var(--font-ui); }
.toast .ok-i { display: inline-block; vertical-align: -3px; margin-inline-end: 6px; color: #4ade80; }
.root.dark .toast .ok-i { color: #15803d; } /* the dark theme's toast is light */

/* ---------------- motion (shared/motion.js sets data-motion on .root: full | subtle | off) ---------------- */
/* off: nothing moves, including the pill, the card, shimmer and spinners */
.root[data-motion="off"] *, .root[data-motion="off"] *::before, .root[data-motion="off"] *::after { animation: none !important; transition: none !important; }

/* full: the card grows out of the point where it was asked for (content.js sets transform-origin) */
.root[data-motion="full"] .card { animation: lm-card-grow .24s cubic-bezier(.2,.9,.3,1.12) both; }
@keyframes lm-card-grow { from { opacity: 0; transform: scale(.9); } 60% { opacity: 1; } to { opacity: 1; transform: none; } }
.root[data-motion="full"] .hero { animation: lm-rise .3s .04s cubic-bezier(.2,.9,.3,1.2) both; }
.root[data-motion="full"] .body > .sec { animation: lm-rise .28s cubic-bezier(.2,.8,.2,1) both; }
.root[data-motion="full"] .body > .sec:nth-of-type(1) { animation-delay: .08s; }
.root[data-motion="full"] .body > .sec:nth-of-type(2) { animation-delay: .13s; }
.root[data-motion="full"] .body > .sec:nth-of-type(n+3) { animation-delay: .18s; }
.root[data-motion="full"] .w-out > * { animation: lm-rise .24s cubic-bezier(.2,.8,.2,1) both; }
.root[data-motion="full"] .milestone { animation: lm-rise .3s cubic-bezier(.2,.9,.3,1.25) both; }
@keyframes lm-rise { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }

/* the word in its sentence gets a highlighter stroke */
.root[data-motion="full"] .hero .ctx mark, .root[data-motion="subtle"] .hero .ctx mark {
  box-shadow: none; background: linear-gradient(var(--accent-soft), var(--accent-soft)) 0 100% / 0 .5em no-repeat;
  animation: lm-marker .45s .28s cubic-bezier(.3,.7,.3,1) forwards;
}
.root[data-motion] .hero .ctx[dir="rtl"] mark { background-position: 100% 100%; }
@keyframes lm-marker { to { background-size: 100% .5em; } }

/* proofreading: each mistake struck through, then its fix (motion.js sequence()) */
.root[data-motion="full"] .lm-seq del {
  text-decoration: none; background: linear-gradient(currentColor, currentColor) 0 58% / 0 1.5px no-repeat;
  animation: lm-strike .22s ease-out calc(160ms + var(--i, 0) * 70ms) forwards;
}
.root[data-motion="full"] .lm-seq ins { display: inline-block; animation: lm-pop .26s cubic-bezier(.2,.9,.3,1.25) calc(160ms + var(--i, 0) * 70ms) both; }
@keyframes lm-strike { to { background-size: 100% 1.5px; } }
@keyframes lm-pop { from { opacity: 0; transform: translateY(4px) scale(.9); } to { opacity: 1; transform: none; } }

/* AI answers word by word (motion.js typeIn()) */
.lm-word { display: inline-block; animation: lm-word .22s cubic-bezier(.2,.8,.2,1) both; }
@keyframes lm-word { from { opacity: 0; transform: translateY(3px); filter: blur(2px); } to { opacity: 1; transform: none; filter: none; } }

/* the Lamha mark while the card waits (shared/motion.js wait()): pages turn for a word, a lens reads a sentence,
   lines are written for the AI. Arabic books turn their pages left → right; the English interface mirrors it */
.brand .dot { position: relative; overflow: hidden; }
.dot .wait { position: absolute; inset: 0; margin: auto; width: 13px; height: 13px; overflow: visible; }
.dot .wait * { transform-box: view-box; }
.dot.waiting > svg:not(.wait) { opacity: 0; }
.wait .cover { fill: rgba(255, 255, 255, .22); }
.wait .pg, .wait .spark { fill: currentColor; stroke: none; }
.wait .pg { opacity: 0; transform-origin: 12px 12px; }
.wait .pg:nth-of-type(3) { opacity: 1; } /* without motion ("subtle"): one page lies open */
.root.en .wait .pg { transform: scaleX(-1); }
.wait .ln { opacity: .45; }
.wait .glass circle { fill: rgba(255, 255, 255, .18); }
.wait .w { transform-origin: 20px 0; }
.root.en .wait .w { transform-origin: 4px 0; }
.wait .spark { transform-origin: 20px 5px; }
.root[data-motion="full"] .wait .pg { animation: lm-turn 1.5s cubic-bezier(.45,.05,.35,1) infinite; }
.root[data-motion="full"] .wait .pg:nth-of-type(4) { animation-delay: .25s; }
.root[data-motion="full"] .wait .pg:nth-of-type(5) { animation-delay: .5s; }
.root.en[data-motion="full"] .wait .pg { animation-name: lm-turn-en; }
.root[data-motion="full"] .wait .glass { animation: lm-scan 2.1s cubic-bezier(.45,.05,.35,1) infinite; }
.root.en[data-motion="full"] .wait .glass { animation-name: lm-scan-en; }
.root[data-motion="full"] .wait .w { animation: lm-write 1.8s cubic-bezier(.3,.6,.3,1) infinite; }
.root[data-motion="full"] .wait .w:nth-of-type(2) { animation-delay: .3s; }
.root[data-motion="full"] .wait .w:nth-of-type(3) { animation-delay: .6s; }
.root[data-motion="full"] .wait .spark { animation: lm-twinkle 1.8s ease-in-out infinite; }
.root[data-motion="subtle"] .dot .wait { animation: lm-breathe 1.6s ease-in-out infinite; }
@keyframes lm-turn { 0% { transform: scaleX(1); opacity: 0; } 12% { opacity: 1; } 70% { transform: scaleX(-1); opacity: 1; } 85%, 100% { transform: scaleX(-1); opacity: 0; } }
@keyframes lm-turn-en { 0% { transform: scaleX(-1); opacity: 0; } 12% { opacity: 1; } 70% { transform: scaleX(1); opacity: 1; } 85%, 100% { transform: scaleX(1); opacity: 0; } }
@keyframes lm-scan { 0%, 100% { transform: translate(2px, -4px); } 26% { transform: translate(-8px, -4px); } 33% { transform: translate(2px, 0); } 59% { transform: translate(-8px, 0); } 66% { transform: translate(2px, 4px); } 92% { transform: translate(-6px, 4px); } }
@keyframes lm-scan-en { 0%, 100% { transform: translate(-8px, -4px); } 26% { transform: translate(2px, -4px); } 33% { transform: translate(-8px, 0); } 59% { transform: translate(2px, 0); } 66% { transform: translate(-8px, 4px); } 92% { transform: translate(0, 4px); } }
@keyframes lm-write { 0% { transform: scaleX(0); opacity: 1; } 35%, 80% { transform: scaleX(1); opacity: 1; } 100% { transform: scaleX(1); opacity: 0; } }
@keyframes lm-twinkle { 0%, 100% { transform: scale(.4); opacity: .4; } 50% { transform: scale(1) rotate(45deg); opacity: 1; } }
@keyframes lm-breathe { 50% { opacity: .55; } }

/* a tool picked with its number key lights up */
.chip.tool.hit { box-shadow: 0 0 0 3px var(--accent-soft); }
.root[data-motion] .chip.tool { transition: background .12s, box-shadow .25s; }
.root[data-motion="full"] .btn:active, .root[data-motion="full"] .chip:active { transform: scale(.96); }

/* before shared/motion.js has read the setting, follow the system's "less motion" wish */
@media (prefers-reduced-motion: reduce) {
  .root:not([data-motion]) *, .root:not([data-motion]) *::before, .root:not([data-motion]) *::after { animation-duration: .001ms !important; animation-iteration-count: 1 !important; transition-duration: .001ms !important; }
}
`;
