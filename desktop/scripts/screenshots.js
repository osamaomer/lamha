/* Lamha desktop — every screen as a picture (npm run shots), for design reviews and before/after checks.
 * Runs like the self-test: from source, with a temporary profile (no API keys, no history of the user's own),
 * light and dark × Arabic and English. The system clipboard is never read or written: sample clips go straight
 * into the store. Pictures go to $LAMHA_SHOTS (default: %TEMP%\lamha-shots), one folder per theme and language.
 * With Ollama running on this PC, the writing tools and AI translation use it, so their results are in the pictures
 * too ($LAMHA_SHOTS_AI=0: no AI, the setup screens instead). */
"use strict";
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const WORD_CTX = { text: "tools", context: { before: "Can differ from the writing ", after: ", e.g. Gemini for writing and Ollama for translation." } };
const SENTENCE = "Honestly, finishing the report was a piece of cake once I got the hang of it.";
const PARAGRAPHS = "The new update ships next week.\n\nIt adds offline translation, a darker theme and faster lookups. Tell us what you think!";
const DRAFT = "i has went to the store yesterday and buyed some apple for my freind.";
const CLIPS = [
  { text: "She dont like apples and she go to school yesterday.", sourceApp: "WhatsApp" },
  { text: "سأرسل لك التقرير النهائي اليوم قبل الساعة الخامسة", sourceApp: "Outlook" },
  { text: "Keep your promise, even when it's hard.", sourceApp: "Notepad" },
  { text: "https://github.com/lamha/lamha/releases", sourceApp: "Firefox" }
];

module.exports = async function shots({ app, mainWin, openOptions, getOptionsWin, stores, send, desktop }) {
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const js = (win, code) => win.webContents.executeJavaScript(code);
  const loaded = win => (win.webContents.isLoading() ? new Promise(r => win.webContents.once("did-finish-load", r)) : Promise.resolve());
  const OUT = process.env.LAMHA_SHOTS || path.join(os.tmpdir(), "lamha-shots");
  setTimeout(() => { console.log("screenshots stuck — giving up"); app.exit(2); }, 15 * 60e3).unref();

  let dir = OUT, n = 0;
  const save = async (win, name) => {
    const file = path.join(dir, `${String(++n).padStart(2, "0")}-${name}.png`);
    if (!win.isVisible()) { win.showInactive(); await wait(300); } // a hidden window never paints: capturePage would wait forever
    const img = await Promise.race([win.webContents.capturePage(), wait(8000).then(() => null)]);
    if (!img) { console.log("  (no picture: " + name + ")"); return; }
    fs.writeFileSync(file, img.toPNG());
    console.log("  " + path.relative(OUT, file));
  };

  mainWin.webContents.setAudioMuted(true);
  await Promise.race([loaded(mainWin), wait(15000)]);
  mainWin.setContentSize(520, 860);
  mainWin.setPosition(40, 40);
  mainWin.showInactive();

  /* ---- the floating card: the extension's card, over a transparent window ---- */
  const cw = desktop.getCardWin();
  await desktop.cardReady();
  cw.setBounds({ x: 600, y: 40, width: 480, height: 620 });
  cw.webContents.setAudioMuted(true);
  const cardMsg = msg => cw.webContents.send("lamha:page-message", { external: true, replaceable: true, point: { x: 240, y: 12 }, ...msg });
  const closeCard = () => js(cw, `document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); true`);
  const card = async (name, msg, ms = 3000) => { cardMsg(msg); await wait(ms); await save(cw, name); };
  /** A real key press on the card (its tools listen on the card itself, inside a closed shadow root). */
  const cardKey = k => { cw.focus(); cw.webContents.focus(); for (const type of ["keyDown", "char", "keyUp"]) cw.webContents.sendInputEvent({ type, keyCode: k }); };
  /** Waits until `expr` is true in `win` (a page whose DOM we can read), at most `ms`. */
  const until = async (win, expr, ms = 90000) => { for (const end = Date.now() + ms; Date.now() < end; await wait(400)) if (await js(win, expr)) return true; return false; };

  /* ---- AI: Ollama on this PC, when it's running ---- */
  let ai = null, aiWait = 0;
  if (process.env.LAMHA_SHOTS_AI !== "0") {
    try {
      const tags = await (await fetch("http://localhost:11434/api/tags", { signal: AbortSignal.timeout(3000) })).json();
      const names = (tags.models || []).map(m => m.name);
      ai = names.find(x => /qwen.*4b/i.test(x)) || names.find(x => /qwen/i.test(x)) || names[0] || null;
    } catch (_) { /* not running: pictures without AI */ }
  }
  if (ai) {
    await stores.local.set({ aiProvider: "ollama", ollamaModel: ai, trProvider: "writing" });
    await send({ type: "ai", tool: "proofread", text: DRAFT, extra: {} }); // loads the model into memory first
    const t0 = Date.now();
    await send({ type: "ai", tool: "improve", text: DRAFT, extra: {} });
    aiWait = Math.min(60000, Math.max(5000, (Date.now() - t0) * 1.6 + 1500)); // the card's answers can't be watched: wait this long
    console.log(`AI: Ollama ${ai}, an answer in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
  } else {
    console.log("AI: none (the writing tools show their setup screen)");
  }

  /* ---- sample data for the review deck and the clipboard (never the real clipboard) ---- */
  const clipStore = desktop.getClipStore && desktop.getClipStore();
  if (clipStore) {
    await stores.local.set({ clipboardEnabled: true }); // the tab and panel show the list, not the opt-in…
    await wait(300);
    desktop.clipboardMonitor.setEnabled(false); // …but nothing copied meanwhile is recorded
    CLIPS.forEach((c, i) => clipStore.ingest({ ...c, capturedAt: Date.now() - (CLIPS.length - i) * 7 * 60e3 }));
  }

  // Wikipedia .zim files, separated by ";" (e.g. Kiwix's wikipedia_ar_top_mini): Settings lists them, the card reads
  // Paris from them, and the reader shows its start page, a search and articles (Karbon too, from a Turkish maxi file)
  const zim = process.env.LAMHA_SHOTS_ZIM;
  const wiki = desktop.getWikiLibrary && desktop.getWikiLibrary();
  if (wiki && wiki.state) { // two sample downloads (never started) so Settings shows a download row: stopped, and paused
    const sample = (flavour, size, got, state, error) => ({ id: `wikipedia_ar_top_${flavour}_2026-07`, file: `wikipedia_ar_top_${flavour}_2026-07.zim`,
      lang: "ar", name: "wikipedia_ar_top", scope: "top", flavour, date: "2026-07-10", articles: 231103, size, got, state, error, mirrors: [], sha256: "", part: "" });
    wiki.state.downloads.push(sample("mini", 226337504, 0, "failed", "offline"), sample("nopic", 925690880, 312e6, "paused", ""));
    wiki.changed(true);
  }
  if (zim && wiki) {
    for (const f of zim.split(";").filter(Boolean)) await wiki.addFile(f).catch(err => console.log("offline Wikipedia file:", err.code || err.message));
  }

  const only = process.env.LAMHA_SHOTS_ONLY; // e.g. "dark-ar": one set, for a quick check
  for (const theme of ["light", "dark"]) {
    for (const lang of ["ar", "en"]) {
      if (only && only !== `${theme}-${lang}`) continue;
      dir = path.join(OUT, `${theme}-${lang}`);
      n = 0;
      fs.mkdirSync(dir, { recursive: true });
      console.log(`${theme} · ${lang}`);
      await stores.sync.set({ theme, uiLang: lang, enDict: false, dictSource: "local" });
      await wait(1800);

      /* card */
      cw.showInactive();
      await card("card-word-in-sentence", { type: "showLookup", ...WORD_CTX });
      await card("card-word-wikipedia", { type: "showLookup", text: "serendipity" }, 4500);
      if (zim && wiki) { // the same part of the card, from the downloaded copy
        await stores.local.set({ wikiOfflineFirst: true });
        await card("card-wikipedia-offline", { type: "showLookup", text: "Paris" }, 2500);
        const b = cw.getContentBounds(); // down to the Wikipedia part, at the end of the card
        cw.webContents.sendInputEvent({ type: "mouseWheel", x: Math.round(b.width / 2), y: Math.round(b.height / 2), deltaX: 0, deltaY: -4000 });
        await wait(700);
        await save(cw, "card-wikipedia-offline-end");
        await stores.local.set({ wikiOfflineFirst: false });
      }
      await stores.sync.set({ enDict: true });
      await wait(300);
      await card("card-english-view", { type: "showLookup", text: "bank", context: { before: "I deposited the check at the ", after: " this morning." } }, 4000);
      await stores.sync.set({ enDict: false });
      await card("card-arabic-word", { type: "showLookup", text: "كتاب" });
      await card("card-sentence", { type: "showLookup", text: SENTENCE });
      if (ai) { // the same sentence translated by the AI (Settings → Translation → AI), with its badge
        await stores.local.set({ trService: "ai" });
        await card("card-sentence-ai", { type: "showLookup", text: SENTENCE + " " }, aiWait); // a space: not the cached answer
        await stores.local.set({ trService: "auto" });
      }
      await card("card-paragraphs", { type: "showLookup", text: PARAGRAPHS });
      cardMsg({ type: "showLookup", text: "The quick brown fox " + theme + lang + " jumps over the lazy dog." });
      await wait(60);
      await save(cw, "card-loading");
      await wait(2500);
      await stores.sync.set({ dictSource: "offline" });
      await wait(300);
      await card("card-error-not-found", { type: "showLookup", text: "blorptastic" }, 1500);
      await stores.sync.set({ dictSource: "local" });
      await card("card-write-selection", { type: "showWrite", text: DRAFT }, 1500);
      if (ai) {
        cardKey("1"); // Proofread
        await wait(aiWait);
        await save(cw, "card-write-proofread");
        cardKey("2"); // Improve
        await wait(aiWait);
        await save(cw, "card-write-improve");
        cardKey("6"); // Longer
        await wait(aiWait * 2); // a longer answer
        await save(cw, "card-write-longer");
      }
      await card("card-write-new", { type: "showWrite", text: "" }, 1500);
      await closeCard();
      // the pill (Firefox: selecting text on a web page) and the page-translation bar, on a stand-in page
      await js(cw, `(() => {
        const p = document.createElement("p");
        p.id = "shotPage";
        p.setAttribute("style", "margin:120px 24px;padding:16px 18px;border-radius:12px;font:16px/1.7 Segoe UI;background:${theme === "dark" ? "#1b1b1f" : "#fff"};color:${theme === "dark" ? "#eee" : "#222"}");
        p.textContent = "Honestly, the meeting was a total rollercoaster from start to finish.";
        document.body.append(p);
        const t = p.firstChild, r = document.createRange();
        r.setStart(t, t.data.indexOf("rollercoaster")); r.setEnd(t, t.data.indexOf("rollercoaster") + 13);
        getSelection().removeAllRanges(); getSelection().addRange(r);
        const b = r.getBoundingClientRect();
        document.dispatchEvent(new PointerEvent("pointerup", { bubbles: true, clientX: b.right, clientY: b.bottom, button: 0 }));
        return true;
      })()`);
      await wait(700);
      await save(cw, "pill");
      cw.webContents.send("lamha:page-message", { type: "pageAction", action: "toggle" });
      await wait(2500);
      await save(cw, "page-bar");
      cw.webContents.send("lamha:page-message", { type: "pageAction", action: "toggle" });
      await wait(400);
      await js(cw, `(() => { getSelection().removeAllRanges(); const p = document.getElementById("shotPage"); if (p) p.remove(); document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); return true; })()`);
      cw.hide();

      /* main window (the popup) */
      const tab = id => js(mainWin, `document.getElementById("${id}").click(); true`);
      const typeQ = async q => { await js(mainWin, `(() => { const q = document.getElementById("q"); q.value = ${JSON.stringify(q)}; q.dispatchEvent(new Event("input", { bubbles: true })); return true; })()`); await wait(2500); };
      await tab("tabTr");
      await typeQ("");
      await save(mainWin, "main-translate-empty");
      await typeQ("reluctant");
      await save(mainWin, "main-translate-word");
      await typeQ("break the ice");
      await save(mainWin, "main-translate-phrase");
      await typeQ("");
      await tab("tabWr");
      await wait(400);
      await save(mainWin, "main-write-empty");
      const wrTool = i => js(mainWin, `(() => { const d = document.getElementById("draft"); d.value = ${JSON.stringify(DRAFT)}; d.dispatchEvent(new Event("input", { bubbles: true })); const b = document.querySelectorAll("#wrTools button")[${i}]; if (b) b.click(); return true; })()`);
      await wrTool(0); // Proofread
      if (ai) {
        await until(mainWin, `!!document.querySelector("#wrOut .text, #wrOut .ok, #wrOut .error")`);
        await wait(1800); // the marks come in one after another (~1.2 s for six)
        await save(mainWin, "main-write-proofread");
        await wrTool(1); // Improve
        await until(mainWin, `!!document.querySelector("#wrOut .text:not(:empty), #wrOut .error")`);
        await wait(1800);
        await save(mainWin, "main-write-improve");
      } else {
        await wait(1500);
        await save(mainWin, "main-write-no-ai");
      }
      await js(mainWin, `(() => { const d = document.getElementById("draft"); d.value = ""; d.dispatchEvent(new Event("input", { bubbles: true })); return true; })()`);
      await tab("tabRv");
      await wait(600);
      await save(mainWin, "main-review-front");
      await js(mainWin, `document.dispatchEvent(new KeyboardEvent("keydown", { key: " " })); true`);
      await wait(600);
      await save(mainWin, "main-review-back");
      if (await js(mainWin, `!!document.getElementById("tabCb")`)) {
        await tab("tabCb");
        await wait(800);
        await save(mainWin, "main-clipboard");
        await js(mainWin, `(() => { const r = document.querySelector("#cbPane .lc-row"); if (r) r.click(); return true; })()`);
        await wait(600);
        await save(mainWin, "main-clipboard-detail");
      }
      await tab("tabTr");

      /* settings */
      openOptions();
      const ow = getOptionsWin();
      await loaded(ow);
      ow.setContentSize(820, 900);
      ow.setPosition(600, 40);
      ow.showInactive();
      await wait(1200);
      for (const id of [null, "dictionary", "wikiPanel", "translation", "ai", "review", "journal", "appearance", "privacy", "updatesPanel", "clipPanel", "cbAppForm"]) {
        const found = await js(ow, id ? `(() => { const s = document.getElementById("${id}"); if (!s || s.offsetParent === null) return false; s.scrollIntoView({ block: "start" }); return true; })()` : "scrollTo(0, 0); true");
        if (!found) continue;
        await wait(700);
        await save(ow, "settings-" + (id || "top").toLowerCase());
      }
      ow.hide();

      /* the Wikipedia reader */
      if (zim && wiki) {
        const rw = desktop.openReader({});
        await loaded(rw);
        rw.setContentSize(1100, 820);
        rw.setPosition(560, 40);
        rw.showInactive();
        await wait(1500);
        await save(rw, "reader-home");
        await js(rw, `(() => { const q = document.getElementById("rdQ"); q.focus(); q.value = "بار"; q.dispatchEvent(new Event("input", { bubbles: true })); return true; })()`);
        await wait(900);
        await save(rw, "reader-search");
        for (const article of ["باريس", "Karbon"]) {
          if (!await wiki.article("", article).catch(() => null)) continue;
          desktop.openReader({ path: article });
          await wait(1800);
          await save(rw, "reader-" + (article === "Karbon" ? "pictures" : "article"));
        }
        rw.setContentSize(620, 820); // narrow: contents behind a button, no floats
        await wait(900);
        await save(rw, "reader-narrow");
        await js(rw, `(() => { const s = document.querySelector(".rd-body h2"); if (s) s.scrollIntoView(); return true; })()`);
        await wait(700);
        await save(rw, "reader-narrow-section");
        rw.hide();
      }

      /* the quick clipboard panel (Win+V-like) */
      if (clipStore) {
        await desktop.openPanel({ target: 0 });
        const pw = desktop.getPanelWin();
        pw.webContents.send("lamha:clip-panel", { enabled: true, paused: false });
        await wait(900);
        await save(pw, "panel-list");
        await js(pw, `(async () => {
          const s = document.querySelector("#cpBody .lc-search");
          s.value = "She dont"; s.dispatchEvent(new Event("input")); // a clip worth proofreading
          await new Promise(r => setTimeout(r, 300));
          s.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
          return true;
        })()`);
        await wait(700);
        await save(pw, "panel-tools");
        if (ai) {
          await js(pw, `(() => { const b = document.querySelector('#cpBody .ca-item[data-act="proofread"]'); if (b) b.click(); return !!b; })()`);
          await until(pw, `!!document.querySelector("#cpBody .ca-text, #cpBody .ca-ok, #cpBody .ca-error")`);
          await wait(1800);
          await save(pw, "panel-proofread");
        }
        desktop.hidePanel();
      }
    }
  }

  console.log("screenshots →", OUT);
  const profile = app.getPath("userData");
  app.exit(0);
  try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); } catch (_) { /* Windows cleans %TEMP% */ }
};
