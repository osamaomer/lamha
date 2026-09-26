/* Lamha desktop self-test (npm run smoke): runs inside the real app with a temporary profile,
 * exercises the windows, the background logic, the disk dictionary, network, speech and Ollama, then quits. */
"use strict";
const fs = require("node:fs");
const path = require("node:path");

/** A tiny Windows Forms window with a text box: stands in for "any app" (WhatsApp, Word…). */
function startTargetApp(text) {
  const { spawn } = require("node:child_process");
  const script = `
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form
$f.Text = "Lamha self-test target"; $f.TopMost = $true; $f.Width = 520; $f.Height = 180; $f.StartPosition = "CenterScreen"
$t = New-Object Windows.Forms.TextBox
$t.Multiline = $true; $t.Dock = "Fill"; $t.Font = New-Object Drawing.Font("Segoe UI", 14); $t.Text = "${text}"
$f.Controls.Add($t)
$f.Add_Shown({ $f.Activate(); $t.Focus(); $t.SelectAll(); [Console]::Out.WriteLine("HWND " + $f.Handle.ToInt64()); [Console]::Out.Flush() })
# report the text box content whenever it changes, so the test can see what was pasted
$script:last = $t.Text
$timer = New-Object Windows.Forms.Timer; $timer.Interval = 100
$timer.Add_Tick({ if ($t.Text -ne $script:last) { $script:last = $t.Text; [Console]::Out.WriteLine("TEXT " + $t.Text); [Console]::Out.Flush() } })
$timer.Start()
[void]$f.ShowDialog()`;
  const proc = spawn("powershell.exe", ["-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-Command", script], { windowsHide: true });
  const state = { text: text };
  const hwnd = new Promise((resolve, reject) => {
    proc.stdout.setEncoding("utf8");
    proc.stdout.on("data", d => {
      for (const line of String(d).split(/\r?\n/)) {
        const m = /^HWND (\d+)/.exec(line);
        if (m) resolve(Number(m[1]));
        if (line.startsWith("TEXT ")) state.text = line.slice(5);
      }
    });
    proc.on("exit", () => reject(new Error("test window closed")));
    setTimeout(() => reject(new Error("test window did not open")), 15000);
  });
  return { proc, hwnd, state };
}

/** A WPF window with a sentence and one word selected in it: WPF text boxes expose their text to UI Automation,
 *  as Word, browsers and Chromium apps (VS Code, Teams…) do; the Windows Forms target above doesn't. */
function startWpfApp(text, word) {
  const { spawn } = require("node:child_process");
  const script = `
Add-Type -AssemblyName PresentationFramework
$w = New-Object Windows.Window; $w.Title = "Lamha self-test (sentence)"; $w.Topmost = $true; $w.Width = 620; $w.Height = 160
$t = New-Object Windows.Controls.TextBox; $t.TextWrapping = "Wrap"; $t.FontSize = 18; $t.Text = ${JSON.stringify(text)}
$w.Content = $t
$w.Add_ContentRendered({ $w.Activate(); [void]$t.Focus(); $t.Select($t.Text.IndexOf(${JSON.stringify(word)}), ${word.length})
  [Console]::Out.WriteLine("HWND " + (New-Object Windows.Interop.WindowInteropHelper $w).Handle.ToInt64()); [Console]::Out.Flush() })
[void]$w.ShowDialog()`;
  const proc = spawn("powershell.exe", ["-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Buffer.from(script, "utf16le").toString("base64")], { windowsHide: true });
  const hwnd = new Promise((resolve, reject) => {
    proc.stdout.setEncoding("utf8");
    proc.stdout.on("data", d => { const m = /HWND (\d+)/.exec(String(d)); if (m) resolve(Number(m[1])); });
    proc.on("exit", () => reject(new Error("test window closed")));
    setTimeout(() => reject(new Error("test window did not open")), 15000);
  });
  return { proc, hwnd };
}

module.exports = async function smoke({ app, mainWin, openOptions, getOptionsWin, stores, send, desktop }) {
  const results = [];
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const js = (win, code) => win.webContents.executeJavaScript(code);
  const loaded = win => (win.webContents.isLoading() ? new Promise(r => win.webContents.once("did-finish-load", r)) : Promise.resolve());
  async function check(name, fn, { optional = false, timeout = 30000 } = {}) {
    const t0 = Date.now();
    let line;
    try {
      const limit = new Promise((_, reject) => setTimeout(() => reject(new Error(`timed out after ${timeout / 1000} s`)), timeout));
      const detail = await Promise.race([fn(), limit]);
      line = `  ✓ ${name}${detail ? " — " + detail : ""} (${Date.now() - t0} ms)`;
    } catch (err) {
      line = `  ${optional ? "!" : "✗"} ${name} — ${err && err.message || err}`;
    }
    results.push(line);
    console.log(line); // progress as it happens
  }
  console.log("Lamha desktop self-test");
  const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
  const smokeCleanups = []; // undone before the results (e.g. the fake network)

  mainWin.webContents.setAudioMuted(true);
  await Promise.race([loaded(mainWin), wait(15000)]);
  await wait(800);
  setTimeout(() => { console.log("self-test stuck — giving up"); app.exit(2); }, 6 * 60e3).unref();

  await check("main window shows the popup with its tabs; browser-only parts hidden", async () => {
    const r = await js(mainWin, `({
      desktop: document.documentElement.classList.contains("desktop"),
      tabs: [...document.querySelectorAll(".tabs button")].map(b => b.textContent.trim()),
      siteCard: getComputedStyle(document.querySelector("main > section.card")).display,
      api: typeof browser.runtime.sendMessage
    })`);
    assert(r.desktop, "desktop class missing");
    const want = desktop && desktop.clipboardMonitor ? 4 : 3; // + الحافظة on Windows
    assert(r.tabs.length === want, "tabs: " + r.tabs.join(", "));
    assert(r.siteCard === "none", "site card visible");
    assert(r.api === "function", "browser API missing");
    return r.tabs.join(" · ");
  });

  await check("offline dictionary read from disk", async () => {
    await stores.sync.set({ dictSource: "offline" });
    const r = await send({ type: "lookup", text: "serendipity" });
    await stores.sync.set({ dictSource: "local" });
    assert(r.ok, r.error);
    assert(r.data.source === "local", "source " + r.data.source);
    return `serendipity → ${r.data.translation || r.data.definitions[0].entries[0].gloss}`;
  });

  await check("page → main process messaging", async () => {
    const q = await js(mainWin, `browser.runtime.sendMessage({ type: "lookup", text: "resilient" }).then(r => r.ok ? r.data.query + " → " + r.data.translation : "ERR " + r.error)`);
    assert(!q.startsWith("ERR"), q);
    return q;
  });

  await check("Google translation over the network", async () => {
    const r = await send({ type: "translateBatch", texts: ["Keep your promise."], tl: "ar" });
    assert(r.ok, r.error);
    assert(/[؀-ۿ]/.test(r.data[0]), "not Arabic: " + r.data[0]);
    return r.data[0];
  }, { optional: true });

  await check("looked-up words became flashcards", async () => {
    await wait(300);
    const r = await send({ type: "reviewQueue" });
    assert(r.counts.total >= 1, "no cards");
    return `${r.counts.total} cards, ${r.counts.fresh} new today`;
  });

  await check("settings saved to disk", async () => {
    await stores.local.set({ smoke: 1 });
    stores.local.flush();
    const file = path.join(app.getPath("userData"), "storage-local.json");
    const data = JSON.parse(fs.readFileSync(file, "utf8"));
    assert(data.smoke === 1 && data.cards, "file content");
    return path.basename(file);
  });

  await check("API keys are encrypted on disk (DPAPI)", async () => {
    const { aiKey: before } = await stores.local.get("aiKey");
    await stores.local.set({ aiKey: "sk-ant-smoke-secret" });
    stores.local.flush();
    const raw = fs.readFileSync(path.join(app.getPath("userData"), "storage-local.json"), "utf8");
    if (before === undefined) await stores.local.remove("aiKey"); else await stores.local.set({ aiKey: before });
    assert(!raw.includes("sk-ant-smoke-secret") && JSON.parse(raw).aiKey.$enc, "key in plain text");
    return "aiKey stored as $enc";
  });

  await check("speech plays through the window", async () => {
    const r = await send({ type: "speak", text: "hello", lang: "en" });
    assert(r.ok, r.error);
  }, { optional: true });

  await check("Ollama proofread from the desktop (no OLLAMA_ORIGINS needed)", async () => {
    const tags = await (await fetch("http://localhost:11434/api/tags")).json();
    const names = tags.models.map(m => m.name);
    const model = names.find(n => /qwen.*4b/i.test(n)) || names.find(n => /qwen/i.test(n)) || names[0];
    assert(model, "no models");
    await stores.local.set({ aiProvider: "ollama", ollamaModel: model });
    const r = await send({ type: "ai", tool: "proofread", text: "She dont like apples and she go to school yesterday.", extra: {} });
    assert(r.ok, r.error);
    return `${model}: "${r.data.corrected}" (${r.data.issues.length} fixes)`;
  }, { optional: true, timeout: 180000 }); // the first request loads the model into memory

  await check("review tab renders a card", async () => {
    await js(mainWin, `document.getElementById("tabRv").click(); true`);
    await wait(500);
    const w = await js(mainWin, `(document.querySelector(".rv-word") || {}).textContent || document.getElementById("rvCard").textContent`);
    assert(w, "empty");
    return w.trim().slice(0, 40);
  });

  await check("settings window: Claude/Ollama panel, journal; Firefox-only parts hidden", async () => {
    openOptions("#journal");
    const win = getOptionsWin();
    await loaded(win);
    await wait(800);
    const r = await js(win, `({
      ai: !!document.getElementById("ai"),
      trigger: getComputedStyle(document.getElementById("triggerPanel")).display,
      originsStep: getComputedStyle(document.querySelector(".ext-only")).display,
      journal: document.getElementById("jSummary").textContent.length > 0,
      version: document.getElementById("ver").textContent
    })`);
    assert(r.ai && r.journal, "panels missing");
    assert(r.trigger === "none" && r.originsStep === "none", "browser-only parts visible");
    return "version " + r.version;
  });

  /* ---- clipboard history: capture (الحافظة) ---- */
  const monitor = desktop && desktop.clipboardMonitor;
  const captured = [];
  /** Clips captured while `fn` runs (plus the debounce and a margin). */
  const capturesDuring = async (fn, settle = 700) => {
    const n = captured.length;
    await fn();
    await wait(settle);
    return captured.slice(n);
  };
  const says = clips => JSON.stringify(clips.map(c => `${c.text.slice(0, 30)} (${c.sourceApp})`));
  if (monitor) {
    const { clipboard, ClipboardItem } = require("electron");
    const { native } = desktop;
    monitor.on("clip-captured", c => captured.push(c));

    await check("clipboard monitor is off by default: copies are not captured", async () => {
      assert(!monitor.enabled, "enabled without the setting");
      const got = await capturesDuring(() => clipboard.writeText("lamha-test-off"));
      assert(got.length === 0, "captured " + says(got));
    });

    await check("enabling via settings: one copy → exactly one capture", async () => {
      await stores.local.set({ clipboardEnabled: true });
      assert(monitor.enabled, "setting did not turn it on");
      const got = await capturesDuring(() => clipboard.writeText("lamha-test-1"));
      assert(got.length === 1, "captures: " + says(got));
      assert(got[0].text === "lamha-test-1" && got[0].sourceApp === "lamha" && got[0].seq > 0 && got[0].capturedAt, "clip " + says(got));
      assert(monitor.recent[0] === got[0], "not first in the ring buffer");
      return `sourceApp ${got[0].sourceApp}, ${monitor.maxSyncMs.toFixed(2)} ms main-thread work`;
    });

    await check("same text copied 3× quickly → debounced to one capture", async () => {
      const got = await capturesDuring(async () => {
        for (let i = 0; i < 3; i++) await clipboard.writeText("lamha-test-repeat");
      });
      assert(got.length === 1, "captures: " + says(got));
    });

    await check("HTML is kept alongside the text", async () => {
      const got = await capturesDuring(() => clipboard.write([new ClipboardItem({ "text/plain": "lamha rich", "text/html": "<b>lamha rich</b>" })]));
      assert(got.length === 1 && got[0].text === "lamha rich", "captures: " + says(got));
      assert(/<b>lamha rich<\/b>/.test(got[0].html || ""), "no HTML");
    });

    await check("Lamha's own writes inside suppress() are not captured", async () => {
      const got = await capturesDuring(() => monitor.suppress(async () => {
        await clipboard.writeText("lamha-test-suppressed");
        await wait(50);
        await clipboard.writeText("lamha-test-suppressed-2");
      }));
      assert(got.length === 0, "captured " + says(got));
    });

    await check("a copy made just before a shortcut (< 150 ms) is still captured", async () => {
      const got = await capturesDuring(async () => {
        await clipboard.writeText("lamha-test-quick");
        await wait(20); // still settling when the shortcut's own clipboard work begins
        await monitor.suppress(async () => { await clipboard.writeText("lamha-test-marker"); await wait(50); });
      });
      assert(got.length === 1 && got[0].text === "lamha-test-quick", "captures: " + says(got));
    });

    await check("password-manager formats are honoured (like Win+V)", async () => {
      const cases = [
        ["ExcludeClipboardContentFromMonitorProcessing", 0, false],
        ["Clipboard Viewer Ignore", 0, false],
        ["CanIncludeInClipboardHistory", 0, false],
        ["CanIncludeInClipboardHistory", 1, true]
      ];
      for (const [format, value, expected] of cases) {
        const text = `lamha-secret-${format}-${value}`;
        const got = await capturesDuring(() => assert(native.writeClipboardRaw(text, { [format]: value }), "raw write failed"));
        assert(got.length === (expected ? 1 : 0), `${format}=${value}: ${says(got)}`);
        if (expected) assert(got[0].text === text, "wrong text");
      }
      return "3 skipped, CanIncludeInClipboardHistory=1 kept";
    });

    await check("empty, whitespace-only and over-long text are not captured", async () => {
      const got = await capturesDuring(async () => {
        await clipboard.writeText("");
        await wait(300);
        await clipboard.writeText("  \n\t  ");
        await wait(300);
        await clipboard.writeText("x".repeat(200001));
      });
      assert(got.length === 0, "captured " + got.map(c => c.text.length + " chars").join(", "));
    });

    await check("disabling stops capture; enabling again resumes it", async () => {
      await stores.local.set({ clipboardEnabled: false });
      const off = await capturesDuring(() => clipboard.writeText("lamha-test-disabled"));
      assert(off.length === 0, "captured while off: " + says(off));
      await stores.local.set({ clipboardEnabled: true });
      const on = await capturesDuring(() => clipboard.writeText("lamha-test-reenabled"));
      assert(on.length === 1, "after re-enabling: " + says(on));
    });

    /* ---- clipboard history: store and search ---- */
    const store = desktop.getClipStore();
    const { ClipboardStore } = require("../clipboard-store");
    const { safeStorage } = require("electron");
    const normalize = globalThis.LamhaArabic.normalizeForSearch;
    const allTexts = s => s.list({ limit: 1000 }).items.map(i => i.text);

    await check("captures are stored; after a restart they are all there (encrypted file)", async () => {
      const want = ["lamha-test-1", "lamha rich", "lamha-test-reenabled"];
      for (const t of want) assert(allTexts(store).includes(t), "missing " + t);
      store.flush();
      const raw = fs.readFileSync(store.file, "utf8");
      const head = JSON.parse(raw);
      const enc = safeStorage.isEncryptionAvailable();
      assert(head.encrypted === enc, `encrypted: ${head.encrypted}, available: ${enc}`);
      if (enc) assert(!raw.includes("lamha-test"), "plain text in the encrypted file");
      const reopened = new ClipboardStore({ file: store.file, normalize, safeStorage }); // what the next start reads
      const back = allTexts(reopened);
      for (const t of want) assert(back.includes(t), "not restored: " + t);
      return `${back.length} clips, ${enc ? "encrypted (DPAPI)" : "NOT encrypted"}`;
    });

    await check("copying the same text again → one clip with copyCount 2", async () => {
      await capturesDuring(() => clipboard.writeText("lamha-test-1"));
      const hits = store.list({ limit: 1000 }).items.filter(i => i.text === "lamha-test-1");
      assert(hits.length === 1 && hits[0].copyCount === 2, JSON.stringify(hits.map(h => h.copyCount)));
      assert(allTexts(store)[0] === "lamha-test-1", "not moved to the top");
    });

    await check("cap: maxItems 5 keeps the newest 5 of 6 copies; a pinned clip survives", async () => {
      const pinnedId = store.list({ query: "lamha rich" }).items[0].id;
      store.setPinned(pinnedId, true);
      await stores.local.set({ clipboardMaxItems: 5 });
      for (let i = 1; i <= 6; i++) await capturesDuring(() => clipboard.writeText("lamha-cap-" + i), 400);
      const now = allTexts(store);
      await stores.local.set({ clipboardMaxItems: 500 });
      assert(JSON.stringify(now) === JSON.stringify(["lamha-cap-6", "lamha-cap-5", "lamha-cap-4", "lamha-cap-3", "lamha-cap-2", "lamha rich"]), JSON.stringify(now));
      assert(store.get(pinnedId).pinned, "pin lost");
    });

    await check("normalizer + store unit tests (tools/test-clipboard.mjs)", async () => {
      const file = path.join(__dirname, "..", "..", "tools", "test-clipboard.mjs");
      if (!fs.existsSync(file)) return "skipped: not in a packaged build";
      const r = require("node:child_process").spawnSync(process.execPath, [file], { env: { ...process.env, ELECTRON_RUN_AS_NODE: "1" }, encoding: "utf8" });
      const last = (r.stdout || "").trim().split("\n").pop();
      assert(r.status === 0, (r.stdout || "").split("\n").filter(l => l.includes("✗")).join("; ") || r.stderr || "exit " + r.status);
      return last;
    }, { timeout: 60000 });

    await check("search over 1,000 clips under 16 ms (in the app)", async () => {
      const tmp = fs.mkdtempSync(path.join(app.getPath("temp"), "lamha-clip-perf-"));
      try {
        const s = new ClipboardStore({ file: path.join(tmp, "clipboard.json"), normalize, maxItems: 1000 });
        const words = ["مَصْرِف", "Invoice_Q3", "meeting", "تقرير", "أحمد", "budget", "مدرسة", "report", "٢٠٢٤", "project"];
        for (let i = 0; i < 1000; i++) {
          s.ingest({ text: `${i} ` + Array.from({ length: 80 }, (_, j) => words[(i + j * 3) % 10]).join(" "), sourceApp: "notepad.exe", capturedAt: 1e12 + i });
        }
        s.list({ query: "warm" });
        let worst = 0;
        for (const q of ["مصرف", "احمد مدرسه", "inv 2024", "nothing-here", ""]) {
          const t0 = performance.now();
          const r = s.list({ query: q });
          worst = Math.max(worst, performance.now() - t0);
          if (q === "مصرف") assert(r.total === 1000, "Arabic variants not matched: " + r.total);
        }
        assert(worst < 16, `slowest ${worst.toFixed(1)} ms`);
        return `slowest query ${worst.toFixed(2)} ms`;
      } finally {
        fs.rmSync(tmp, { recursive: true, force: true });
      }
    });

    await check("corrupt history file → empty history, file kept", async () => {
      const tmp = fs.mkdtempSync(path.join(app.getPath("temp"), "lamha-clip-corrupt-"));
      try {
        const file = path.join(tmp, "clipboard.json");
        fs.writeFileSync(file, JSON.stringify({ v: 1, encrypted: true, data: "bm90IGVuY3J5cHRlZA==" }));
        const s = new ClipboardStore({ file, normalize, safeStorage });
        assert(s.list().total === 0, "not empty");
        const kept = fs.readdirSync(tmp).filter(f => f.startsWith("clipboard.corrupt-"));
        assert(kept.length === 1 && !fs.existsSync(file), fs.readdirSync(tmp).join(", "));
        return kept[0];
      } finally {
        fs.rmSync(tmp, { recursive: true, force: true });
      }
    });

    await check("pages reach the history over IPC (search, label, changed event); the card can't", async () => {
      const r = await js(mainWin, `(async () => {
        let events = 0;
        lamhaClipboard.onChanged(() => events++);
        const found = await lamhaClipboard.list({ query: "LAMHA CAP" });
        const id = found.data.items[0].id;
        const label = await lamhaClipboard.setLabel(id, "تجربة الحافظة");
        const byLabel = await lamhaClipboard.list({ query: "تجربه" });
        const full = await lamhaClipboard.get(id);
        const bad = await lamhaClipboard.markUsed("c_missing");
        await new Promise(r => setTimeout(r, 400));
        return { total: found.data.total, label: label.data, hit: byLabel.data.items[0].id === id, full: full.data.text, bad: bad.error, events };
      })()`);
      assert(r.total === 5, "search total " + r.total);
      assert(r.label === "تجربة الحافظة" && r.hit, "label search failed");
      assert(r.full.startsWith("lamha-cap-"), "get failed");
      assert(r.bad === "not_found", "error code " + r.bad);
      assert(r.events >= 1, "no changed event");
      const cw = desktop.getCardWin();
      assert((await js(cw, `typeof window.lamhaClipboard`)) === "undefined", "card has the history API");
      return `${r.total} results, ${r.events} changed event(s)`;
    });

    /* ---- clipboard history: quick panel and tab ---- */
    const panel = desktop.getPanelWin();
    /** Types into the panel's search box and returns the visible rows' text. */
    const panelSearch = q => js(panel, `(async () => {
      const s = document.querySelector("#cpBody .lc-search");
      s.value = ${JSON.stringify(q)};
      s.dispatchEvent(new Event("input"));
      await new Promise(r => setTimeout(r, 250));
      return [...document.querySelectorAll("#cpBody .lc-row .lc-text")].map(e => e.textContent);
    })()`);
    const panelKey = (key, mods = {}) => js(panel, `(() => {
      const s = document.querySelector("#cpBody .lc-search");
      s.dispatchEvent(new KeyboardEvent("keydown", { key: ${JSON.stringify(key)}, code: ${JSON.stringify(mods.code || "")}, shiftKey: ${!!mods.shift}, ctrlKey: ${!!mods.ctrl}, bubbles: true, cancelable: true }));
      return true;
    })()`);

    await check("quick panel: opens within 150 ms, search filters and highlights, Esc closes", async () => {
      await desktop.panelReady();
      const t0 = performance.now();
      await desktop.openPanel({ target: 0 });
      const ms = performance.now() - t0;
      assert(panel.isVisible(), "not visible");
      await wait(300);
      const all = await js(panel, `({ rows: document.querySelectorAll("#cpBody .lc-row").length, focused: document.activeElement && document.activeElement.classList.contains("lc-search") })`);
      assert(all.rows >= 5, "rows " + all.rows);
      const rows = await panelSearch("LAMHA CAP");
      assert(rows.length === 5 && rows.every(t => t.startsWith("lamha-cap-")), JSON.stringify(rows));
      const marks = await js(panel, `document.querySelectorAll("#cpBody .lc-row mark").length`);
      assert(marks >= 5, "no highlight");
      await panelKey("Escape");
      await wait(200);
      assert(!panel.isVisible(), "Esc did not close");
      assert(ms < 150, `took ${ms.toFixed(0)} ms`);
      return `visible in ${ms.toFixed(0)} ms, ${all.rows} rows${all.focused ? ", search focused" : ""}`;
    });

    await check("feature off: Alt+Shift+V shows the opt-in card; enabling there shows the list", async () => {
      await stores.local.set({ clipboardEnabled: false });
      await desktop.openPanel({ target: 0 });
      await wait(250);
      const card = await js(panel, `(() => { const c = document.querySelector("#cpBody .lc-optin"); return c ? c.textContent : ""; })()`);
      assert(card.includes("سجل الحافظة") && card.includes("تفعيل سجل الحافظة"), "no opt-in card");
      await js(panel, `document.querySelector("#cpBody .lc-optin .btn").click(); true`);
      await wait(400);
      assert(monitor.enabled, "not enabled");
      const rows = await js(panel, `document.querySelectorAll("#cpBody .lc-row").length`);
      assert(rows >= 5, "list not shown: " + rows);
      desktop.hidePanel();
    });

    await check("Delete removes a clip with a 5 s undo; undo restores it", async () => {
      await desktop.openPanel({ target: 0 });
      await wait(250);
      const rows = await panelSearch("lamha-cap-6");
      assert(rows.length === 1, JSON.stringify(rows));
      const id = store.list({ query: "lamha-cap-6" }).items[0].id;
      await panelKey("Delete");
      await wait(300);
      assert(!store.get(id), "not deleted");
      const toast = await js(panel, `(document.querySelector("#cpBody .lc-toast") || {}).textContent || ""`);
      assert(toast.includes("تراجع"), "no undo toast");
      await js(panel, `document.querySelector("#cpBody .lc-undo").click(); true`);
      await wait(300);
      assert(store.get(id) && store.get(id).text === "lamha-cap-6", "not restored");
      desktop.hidePanel();
    });

    await check("الحافظة tab: opt-in card when off; list and detail when on", async () => {
      await stores.local.set({ clipboardEnabled: false });
      await js(mainWin, `document.getElementById("tabCb").click(); true`);
      await wait(300);
      const off = await js(mainWin, `({ optin: !!document.querySelector("#cbPane .lc-optin"), others: ["trPane", "wrPane", "rvPane"].every(p => document.getElementById(p).hidden) })`);
      assert(off.optin && off.others, JSON.stringify(off));
      await stores.local.set({ clipboardEnabled: true });
      await wait(400);
      const on = await js(mainWin, `(async () => {
        const rows = document.querySelectorAll("#cbPane .lc-row");
        const n = rows.length;
        rows[0].click();
        await new Promise(r => setTimeout(r, 300));
        const full = (document.querySelector("#cbPane .cb-full") || {}).textContent;
        const buttons = [...document.querySelectorAll("#cbPane .cb-acts button")].map(b => b.textContent);
        document.querySelector("#cbPane .cb-dhead .link").click();
        await new Promise(r => setTimeout(r, 200));
        return { n, full, buttons, back: !document.querySelector("#cbPane .cb-detail").hidden ? "still in detail" : "list" };
      })()`);
      assert(on.n >= 5 && on.full && on.back === "list", JSON.stringify(on));
      assert(JSON.stringify(on.buttons) === JSON.stringify(["نسخ", "تثبيت", "تسمية", "حذف"]) || on.buttons.includes("إلغاء التثبيت"), JSON.stringify(on.buttons));
      await js(mainWin, `document.getElementById("tabTr").click(); true`);
      const hidden = await js(mainWin, `document.getElementById("cbPane").hidden && !document.getElementById("trPane").hidden`);
      assert(hidden, "switching back to ترجمة failed");
      return `${on.n} rows, detail: ${on.buttons.join(" · ")}`;
    });

    await check("tab نسخ copies a clip with its formatting, counts as used, captures nothing", async () => {
      const id = store.list({ query: "lamha rich" }).items[0].id;
      const used = store.get(id).useCount;
      const got = await capturesDuring(() => js(mainWin, `lamhaClipboard.copy(${JSON.stringify(id)})`));
      assert(got.length === 0, "captured " + says(got));
      assert((await clipboard.readText()) === "lamha rich", "clipboard text");
      assert((await clipboard.read()).flatMap(i => i.types).includes("text/html"), "formatting lost");
      assert(store.get(id).useCount === used + 1, "not marked used");
    });

    /* ---- Lamha's tools on clips (Phase 4), against a fake network like tools/test-writing.mjs ---- */
    const realFetch = globalThis.fetch;
    const net = { google: 0, claude: 0 };
    const fakeJson = (status, body) => ({ ok: status < 300, status, json: async () => body, text: async () => JSON.stringify(body) });
    globalThis.fetch = async (url, init) => { // background.js looks up `fetch` at call time
      const u = String(url);
      if (u.includes("/translate_a/single")) {
        net.google++;
        const q = new URL(u).searchParams.get("q") || "";
        if (q === "bank") {
          return fakeJson(200, { src: "en", sentences: [{ trans: "مصرف", orig: "bank" }],
            definitions: [{ pos: "noun", entry: [{ gloss: "a financial institution", example: "he cashed a check at the bank", definition_id: "d1" }] }] });
        }
        return fakeJson(200, { src: "en", sentences: [{ trans: "حافظ على وعدك.", orig: q }] });
      }
      if (u.startsWith("https://api.anthropic.com/")) {
        net.claude++;
        const prompt = JSON.parse(init.body).messages[0].content;
        const reply = /^Proofread/.test(prompt)
          ? { corrected: "She doesn't like apples.", issues: [{ original: "dont", fix: "doesn't", category: "agreement", why: "مع she يأتي doesn't." }] }
          : /^Summarize/.test(prompt) ? { text: /in English/.test(prompt) ? "• Growth this quarter." : "• نمو في هذا الربع." }
          : { text: "I will send you the report today." };
        return fakeJson(200, { stop_reason: "end_turn", content: [{ type: "text", text: JSON.stringify(reply) }] });
      }
      return realFetch(url, init);
    };
    smokeCleanups.push(() => { globalThis.fetch = realFetch; });
    await stores.sync.set({ dictSource: "online", translateDefinitions: false });
    /** A key on whatever has focus in the panel (the tools view has no search box). */
    const panelKeyHere = key => js(panel, `(() => {
      document.activeElement.dispatchEvent(new KeyboardEvent("keydown", { key: ${JSON.stringify(key)}, bubbles: true, cancelable: true }));
      return true;
    })()`);
    const openActionsFor = async query => {
      if (!panel.isVisible()) await desktop.openPanel({ target: 0 });
      await wait(250);
      await panelSearch(query);
      await panelKey("Tab");
      await wait(300);
      return js(panel, `[...document.querySelectorAll("#cpBody .ca-item")].map(b => b.dataset.act)`);
    };
    const runAction = async act => {
      await js(panel, `document.querySelector('#cpBody .ca-item[data-act="${act}"]').click(); true`);
      await wait(500);
      return js(panel, `({ text: (document.querySelector("#cpBody .ca-text") || {}).textContent || "", ok: (document.querySelector("#cpBody .ca-ok") || {}).textContent || "",
        error: (document.querySelector("#cpBody .ca-error") || {}).textContent || "", cached: !!document.querySelector("#cpBody .ca-muted"),
        buttons: [...document.querySelectorAll("#cpBody .ca-acts button")].map(b => b.textContent), focused: document.activeElement.textContent })`);
    };

    await check("clips are tagged with their language (backfilled on load)", async () => {
      await capturesDuring(() => clipboard.writeText("Keep your promise."));
      await capturesDuring(() => clipboard.writeText("سأرسل لك التقرير اليوم"));
      const langs = Object.fromEntries(store.list({ limit: 1000 }).items.map(i => [i.text, i.lang]));
      assert(langs["Keep your promise."] === "en" && langs["سأرسل لك التقرير اليوم"] === "ar", JSON.stringify(langs));
      store.flush();
      const back = new ClipboardStore({ file: store.file, normalize, safeStorage, detectLang: globalThis.LamhaArabic.detectLang });
      assert(back.list({ query: "keep your" }).items[0].lang === "en", "not kept");
    });

    await check("Tab in the panel: only the tools that fit the clip", async () => {
      const en = await openActionsFor("keep your promise");
      assert(JSON.stringify(en) === JSON.stringify(["translate", "proofread", "lookup", "plain"]), "English: " + en);
      await panelKeyHere("Escape"); // tools → list (the panel stays open)
      await wait(150);
      assert(panel.isVisible(), "Esc in the tools closed the panel");
      const ar = await openActionsFor("سأرسل لك التقرير");
      assert(JSON.stringify(ar) === JSON.stringify(["english", "plain"]), "Arabic: " + ar);
      desktop.hidePanel();
      return `en: ${en.join(", ")} · ar: ${ar.join(", ")}`;
    });

    await check("ترجم shows a preview (لصق / نسخ / إعادة), is cached on the clip, and the list shows it", async () => {
      const before = net.google;
      await openActionsFor("keep your promise");
      const r = await runAction("translate");
      assert(r.text === "حافظ على وعدك.", JSON.stringify(r));
      assert(JSON.stringify(r.buttons) === JSON.stringify(["لصق", "نسخ", "إعادة"]) && r.focused === "لصق", JSON.stringify(r));
      assert(net.google === before + 1, "requests: " + (net.google - before));
      desktop.hidePanel();
      await openActionsFor("keep your promise");
      const again = await runAction("translate");
      assert(again.cached && again.text === "حافظ على وعدك.", "not from the cache");
      assert(net.google === before + 1, "the second open went to the network");
      await js(panel, `document.querySelector("#cpBody .ca-head .link").click(); true`);
      await wait(300);
      const line = await js(panel, `(document.querySelector("#cpBody .lc-tr") || {}).textContent || ""`);
      assert(line === "حافظ على وعدك.", "list line: " + line);
      desktop.hidePanel();
    });

    await check("writing tools without a provider: the existing Arabic message and a Settings button", async () => {
      await stores.local.set({ aiProvider: "claude" });
      await stores.local.remove("aiKey");
      await openActionsFor("keep your promise");
      const r = await runAction("proofread");
      assert(r.error.includes("فعّل أدوات الكتابة") && r.error.includes("الإعدادات"), JSON.stringify(r.error));
      assert(net.claude === 0, "a request was made");
      desktop.hidePanel();
    });

    await check("اكتبه بالإنجليزية with a provider: preview, then cached", async () => {
      await stores.local.set({ aiProvider: "claude", aiKey: "sk-smoke" });
      await openActionsFor("سأرسل لك التقرير");
      const r = await runAction("english");
      assert(r.text === "I will send you the report today.", JSON.stringify(r));
      assert(net.claude === 1, "requests " + net.claude);
      const id = store.list({ query: "سأرسل لك التقرير" }).items[0].id;
      assert(store.get(id).cache.english.text === r.text, "not cached on the clip");
      desktop.hidePanel();
    });

    await check("لخّص: switch the summary language; each is cached, the choice is remembered", async () => {
      await capturesDuring(() => clipboard.writeText("The quarterly report shows steady growth in every region. ".repeat(10)));
      const tools = await openActionsFor("quarterly report");
      assert(tools.includes("summary"), "no summary for a long clip: " + tools);
      const calls = net.claude;
      const ar = await runAction("summary");
      assert(ar.text === "• نمو في هذا الربع.", JSON.stringify(ar));
      const langBtns = () => js(panel, `[...document.querySelectorAll("#cpBody .ca-lang button")].map(b => b.textContent + ":" + b.getAttribute("aria-pressed"))`);
      assert(JSON.stringify(await langBtns()) === JSON.stringify(["العربية:true", "English:false"]), "switch: " + (await langBtns()));
      await js(panel, `document.querySelector('#cpBody .ca-lang button[lang="en"]').click(); true`);
      await wait(500);
      const en = await js(panel, `document.querySelector("#cpBody .ca-text").textContent`);
      assert(en === "• Growth this quarter.", "English summary: " + en);
      assert((await stores.local.get("clipboardSummaryLang")).clipboardSummaryLang === "en", "choice not remembered");
      await js(panel, `document.querySelector('#cpBody .ca-lang button[lang="ar"]').click(); true`);
      await wait(400);
      assert(net.claude === calls + 2, "requests: " + (net.claude - calls) + " (Arabic again should be cached)");
      await stores.local.set({ clipboardSummaryLang: "en" });
      desktop.hidePanel();
      await openActionsFor("quarterly report");
      const again = await runAction("summary"); // opens in the remembered language, from the cache
      assert(again.text === "• Growth this quarter." && again.cached, JSON.stringify(again));
      desktop.hidePanel();
    });


    /* ---- Phase 5: privacy rules at capture ---- */
    await check("excluded programs are never recorded (password managers by default; added and removed ones)", async () => {
      try {
        monitor.sourceAppOverride = "keepassxc.exe"; // test-only hook: pretend the copy comes from this program
        const pm = await capturesDuring(() => clipboard.writeText("lamha-secret-from-keepassxc"));
        assert(pm.length === 0, "password manager recorded");
        monitor.sourceAppOverride = "notepad.exe";
        const added = await js(mainWin, `lamhaClipboard.excludeApp("Notepad").then(r => r.data)`);
        assert(added === "notepad.exe", "normalized to " + added);
        await wait(100);
        const off = await capturesDuring(() => clipboard.writeText("lamha-from-excluded-notepad"));
        assert(off.length === 0, "excluded program recorded");
        await js(mainWin, `lamhaClipboard.includeApp("notepad.exe")`);
        await wait(100);
        const on = await capturesDuring(() => clipboard.writeText("lamha-from-notepad-again"));
        assert(on.length === 1 && on[0].sourceApp === "notepad.exe", "after removing the exclusion: " + says(on));
      } finally {
        monitor.sourceAppOverride = null;
      }
    });

    await check("bank-card numbers are skipped (Luhn); phone numbers and invalid numbers are kept", async () => {
      const card = await capturesDuring(() => clipboard.writeText("4111 1111 1111 1111"));
      assert(card.length === 0, "card number recorded");
      const others = [];
      for (const t of ["4111 1111 1111 1112", "+966 50 123 4567"]) others.push(...await capturesDuring(() => clipboard.writeText(t)));
      assert(others.length === 2, "kept: " + others.length);
      await stores.local.set({ clipboardSkipCards: false });
      const offSetting = await capturesDuring(() => clipboard.writeText("5555 5555 5555 4444"));
      await stores.local.set({ clipboardSkipCards: true });
      assert(offSetting.length === 1, "setting off: " + offSetting.length);
    });

    await check("pause: nothing recorded, banner in the panel, استئناف resumes; timed pauses end by themselves", async () => {
      desktop.pauseClipboard(0); // until resumed
      assert(desktop.trayTooltip().includes("الحافظة متوقفة مؤقتًا"), "tooltip: " + desktop.trayTooltip());
      const paused = await capturesDuring(() => clipboard.writeText("lamha-while-paused"));
      assert(paused.length === 0, "recorded while paused");
      await desktop.openPanel({ target: 0 });
      await wait(250);
      const banner = await js(panel, `(() => { const b = document.querySelector(".cp-banner"); return b && !b.hidden ? b.textContent : ""; })()`);
      assert(banner.includes("الحافظة متوقفة مؤقتًا"), "no banner");
      await js(panel, `document.querySelector(".cp-banner button").click(); true`);
      await wait(200);
      desktop.hidePanel();
      assert(!desktop.trayTooltip().includes("متوقفة"), "still paused after استئناف");
      const resumed = await capturesDuring(() => clipboard.writeText("lamha-after-resume"));
      assert(resumed.length === 1, "not recorded after resume");
      desktop.pauseClipboard(500);
      const timed = await capturesDuring(() => clipboard.writeText("lamha-timed-pause"), 200);
      await wait(500);
      const after = await capturesDuring(() => clipboard.writeText("lamha-after-timed-pause"));
      assert(timed.length === 0 && after.length === 1, `timed: ${timed.length}, after: ${after.length}`);
    });

    await check("الحافظة tab: أضف للمراجعة adds the word, pressing again removes it (🔖)", async () => {
      await capturesDuring(() => clipboard.writeText("bank"));
      await js(mainWin, `document.getElementById("tabCb").click(); true`);
      await wait(300);
      const press = () => js(mainWin, `(async () => {
        const s = document.querySelector("#cbPane .lc-search");
        s.value = "bank"; s.dispatchEvent(new Event("input"));
        await new Promise(r => setTimeout(r, 250));
        if (document.querySelector("#cbPane .cb-detail").hidden) document.querySelector("#cbPane .lc-row").click(); // open "bank"
        await new Promise(r => setTimeout(r, 300));
        const btn = document.querySelector('#cbPane .ca-item[data-act="review"]');
        if (!btn) return "no review button; rows: " + [...document.querySelectorAll("#cbPane .lc-row .lc-text")].map(e => e.textContent).join(" | ")
          + "; tools: " + [...document.querySelectorAll("#cbPane .ca-item")].map(b => b.dataset.act).join(",") + "; detail: " + !document.querySelector("#cbPane .cb-detail").hidden;
        btn.click();
        await new Promise(r => setTimeout(r, 700));
        return (document.querySelector("#cbPane .ca-ok") || document.querySelector("#cbPane .ca-error") || {}).textContent || "";
      })()`);
      const added = await press();
      assert(added.includes("أُضيفت") && (await send({ type: "cardHas", q: "bank" })), "add: " + added);
      const card = (await stores.local.get("cards")).cards.bank;
      assert(card.ex === "he cashed a check at the bank", "example " + JSON.stringify(card.ex));
      const removed = await press();
      assert(removed.includes("أُزيلت") && !(await send({ type: "cardHas", q: "bank" })), "remove: " + removed);
      await js(mainWin, `document.querySelector("#cbPane .cb-dhead .link").click(); document.getElementById("tabTr").click(); true`);
      return added.split("\n")[0];
    });
  }

  /* ---- any app: the sentence around the selected word (UI Automation), for «فهم الكلمة من سياق الجملة» ---- */
  if (desktop && desktop.selection && desktop.uiaContext) {
    const { selection, native, uiaContext } = desktop;
    const app = startWpfApp("Install the Lamha extension in Firefox, then open its settings.", "extension");
    try {
      await check("lookup in another app reads the sentence around the word", async () => {
        uiaContext.start(); // the self-test doesn't start it with the app
        const hwnd = await app.hwnd;
        await wait(1500); // the window and the helper start
        for (let i = 0; i < 5 && native.foreground() !== hwnd; i++) { native.forceForeground(hwnd); await wait(200); }
        assert(native.foreground() === hwnd, "test app not in front: " + native.className(native.foreground()));
        const cap = await selection.captureSelection();
        assert(cap.text === "extension", "copied " + JSON.stringify(cap.text));
        const ctx = await uiaContext.around(cap.hwnd, cap.text, 5000);
        assert(ctx && ctx.before === "Install the Lamha " && ctx.after.startsWith(" in Firefox"), "context " + JSON.stringify(ctx));
        const r = await send({ type: "lookup", text: cap.text, context: ctx });
        assert(r.ok, "lookup failed: " + r.error);
        assert(r.data.context || r.data.contextSense, "the lookup didn't use the sentence");
        return `"${ctx.before}[extension]${ctx.after.slice(0, 20)}…"`;
      });
      await check("the sentence is not read when the app's selection is other text", async () => {
        const hwnd = await app.hwnd;
        assert((await uiaContext.around(hwnd, "Firefox", 3000)) === null, "read text around a word that isn't selected");
      });
    } finally {
      app.proc.kill();
      uiaContext.stop();
    }
  }

  /* ---- any app: global shortcut flow against a real Windows text box ---- */
  if (desktop && desktop.selection) {
    const { selection, native, onHotkey, getCardWin } = desktop;
    const { clipboard } = require("electron");
    const target = startTargetApp("She dont like apples.");
    let hwnd = 0;
    const USER_CLIP = "the user's own clipboard text";
    try {
      await check("test app opened with its text selected", async () => {
        hwnd = await target.hwnd;
        await wait(400);
        for (let i = 0; i < 5 && native.foreground() !== hwnd; i++) { native.forceForeground(hwnd); await wait(200); }
        assert(native.foreground() === hwnd, "not in front: " + native.className(native.foreground()));
        return native.className(hwnd).slice(0, 24);
      });

      if (monitor && monitor.enabled) {
        await check("a copy made in another app is captured with its program name", async () => {
          native.forceForeground(hwnd);
          await wait(150);
          // never send keys to a window the test doesn't own (e.g. if the user clicked elsewhere)
          assert(native.foreground() === hwnd, "test app not in front: " + native.className(native.foreground()));
          const got = await capturesDuring(async () => {
            await native.releaseModifiers();
            native.ctrlChord(native.VK.C);
          });
          assert(got.length === 1, "captures: " + says(got));
          assert(got[0].text === "She dont like apples.", "text " + JSON.stringify(got[0].text));
          assert(got[0].sourceApp === "powershell.exe", "sourceApp " + got[0].sourceApp);
          return got[0].sourceApp;
        });
      }
      const beforeShortcuts = captured.length;

      await check("reads the selection from another app; clipboard left untouched", async () => {
        const { ClipboardItem } = require("electron");
        await clipboard.write([new ClipboardItem({ "text/plain": USER_CLIP, "text/html": `<b>${USER_CLIP}</b>` })]); // rich copy
        native.forceForeground(hwnd);
        await wait(150);
        assert(native.foreground() === hwnd, "test app not in front: " + native.className(native.foreground()));
        const cap = await selection.captureSelection();
        assert(cap.text === "She dont like apples.", JSON.stringify(cap.text));
        assert(cap.hwnd === hwnd, "wrong window");
        const now = await clipboard.readText();
        assert(now === USER_CLIP, "clipboard changed: " + now);
        const types = (await clipboard.read()).flatMap(i => i.types);
        assert(types.includes("text/html"), "formatting lost: " + types.join(", "));
        return JSON.stringify(cap.text);
      });

      await check("shortcut opens the card over the app, focused", async () => {
        native.forceForeground(hwnd);
        await wait(150);
        await onHotkey("write");
        await wait(700);
        const cw = getCardWin();
        assert(cw.isVisible(), "card window hidden");
        const has = await js(cw, `!!document.querySelector("lamha-ui")`);
        assert(has, "card not rendered");
        const focused = native.foreground() === native.hwndOf(cw);
        if (process.env.LAMHA_SHOTS) { // the real screen: the card floating over the test app
          const { desktopCapturer, screen } = require("electron");
          const d = screen.getPrimaryDisplay();
          const [src] = await desktopCapturer.getSources({ types: ["screen"], thumbnailSize: { width: d.size.width * d.scaleFactor, height: d.size.height * d.scaleFactor } });
          const b = cw.getBounds(), s = d.scaleFactor;
          const crop = { x: Math.max(0, (b.x - 300) * s), y: Math.max(0, (b.y - 200) * s), width: (b.width + 600) * s, height: (b.height + 220) * s };
          fs.mkdirSync(process.env.LAMHA_SHOTS, { recursive: true });
          fs.writeFileSync(path.join(process.env.LAMHA_SHOTS, "5-any-app.png"), src.thumbnail.crop(crop).toPNG());
        }
        return focused ? "card has keyboard focus" : "card shown (Windows kept focus on the app)";
      });

      await check("Replace pastes the result back into the app; clipboard restored", async () => {
        const cw = getCardWin();
        const ok = await js(cw, `window.lamhaDesktop.replace("She doesn't like apples.")`);
        assert(ok === true, "replace returned " + ok);
        await wait(400);
        assert(!cw.isVisible(), "card still visible");
        assert(target.state.text === "She doesn't like apples.", "app now has: " + JSON.stringify(target.state.text));
        assert((await clipboard.readText()) === USER_CLIP, "clipboard changed");
        return "app now has " + JSON.stringify(target.state.text);
      });

      await check("nothing selected: a hint instead of a card", async () => {
        native.forceForeground(hwnd);
        await wait(150);
        native.tap(native.VK.RIGHT); // collapse the selection
        await wait(100);
        await onHotkey("lookup");
        const cw = getCardWin();
        assert(cw.isVisible(), "hint window not shown");
        await wait(1900);
        assert(!cw.isVisible(), "hint did not go away");
      });

      if (monitor && monitor.enabled) {
        await check("shortcut Ctrl+C, Replace and clipboard restores created no clips", async () => {
          await wait(500);
          const own = captured.slice(beforeShortcuts).filter(c => c.text !== USER_CLIP); // the test's own "user copy" may count
          assert(own.length === 0, "captured " + says(own));
        });

        const panel = desktop.getPanelWin();
        const store = desktop.getClipStore();
        const pasteVia = async (query, mods) => {
          native.forceForeground(hwnd);
          await wait(200);
          assert(native.foreground() === hwnd, "test app not in front: " + native.className(native.foreground()));
          await desktop.openPanel(); // remembers the test app as the paste target
          await wait(300);
          await js(panel, `(async () => {
            const s = document.querySelector("#cpBody .lc-search");
            s.value = ${JSON.stringify(query)};
            s.dispatchEvent(new Event("input"));
            await new Promise(r => setTimeout(r, 250));
            s.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", shiftKey: ${!!mods.shift}, bubbles: true, cancelable: true }));
            return true;
          })()`);
          await wait(1200);
        };

        await check("quick panel pastes a clip into the app in front; no new clip; stays on the clipboard", async () => {
          const id = store.list({ query: "lamha rich" }).items[0].id;
          const used = store.get(id).useCount;
          const before = target.state.text;
          const got = await capturesDuring(() => pasteVia("lamha rich", {}), 300);
          assert(!panel.isVisible(), "panel still open");
          assert(target.state.text === before + "lamha rich", "app has " + JSON.stringify(target.state.text));
          assert(got.length === 0, "captured " + says(got));
          assert((await clipboard.read()).flatMap(i => i.types).includes("text/html"), "formatting not on the clipboard");
          assert(store.get(id).useCount === used + 1, "not marked used");
          return "app now has " + JSON.stringify(target.state.text);
        });

        await check("Shift+Enter pastes plain text (no HTML on the clipboard)", async () => {
          const before = target.state.text;
          const got = await capturesDuring(() => pasteVia("lamha rich", { shift: true }), 300);
          assert(target.state.text === before + "lamha rich", "app has " + JSON.stringify(target.state.text));
          const types = (await clipboard.read()).flatMap(i => i.types);
          assert(!types.includes("text/html"), "HTML still there: " + types.join(", "));
          assert(got.length === 0, "captured " + says(got));
        });

        await check("no app to paste into (tray / window gone): only copies, with a notice", async () => {
          await clipboard.writeText("something else");
          await wait(400);
          await desktop.openPanel({ target: 0 });
          await wait(300);
          const got = await capturesDuring(() => js(panel, `(async () => {
            const s = document.querySelector("#cpBody .lc-search");
            s.value = "lamha rich"; // pinned, so the earlier cap test kept it
            s.dispatchEvent(new Event("input"));
            await new Promise(r => setTimeout(r, 250));
            return lamhaClipboard.paste(document.querySelector("#cpBody .lc-row").dataset.id, false);
          })()`), 500);
          assert(got.length === 0, "captured " + says(got));
          assert((await clipboard.readText()) === "lamha rich", "not copied");
        });

        await check("a tool's result is pasted only after its preview (لصق); no new clip", async () => {
          native.forceForeground(hwnd);
          await wait(200);
          assert(native.foreground() === hwnd, "test app not in front: " + native.className(native.foreground()));
          await desktop.openPanel(); // the test app is the paste target
          await wait(300);
          const before = target.state.text;
          await js(panel, `(async () => {
            const s = document.querySelector("#cpBody .lc-search");
            s.value = "She dont like"; s.dispatchEvent(new Event("input"));
            await new Promise(r => setTimeout(r, 250));
            s.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
            await new Promise(r => setTimeout(r, 300));
            document.querySelector('#cpBody .ca-item[data-act="proofread"]').click();
            return true;
          })()`);
          await wait(600);
          const preview = await js(panel, `({ ins: document.querySelectorAll("#cpBody .ca-text ins").length, why: (document.querySelector("#cpBody .ca-why") || {}).textContent })`);
          assert(preview.ins >= 1 && preview.why, "no diff preview: " + JSON.stringify(preview));
          assert(target.state.text === before, "pasted before لصق was pressed");
          const got = await capturesDuring(async () => {
            await js(panel, `document.activeElement.click(); true`); // لصق has the focus (Enter)
            await wait(1200);
          }, 300);
          assert(target.state.text === before + "She doesn't like apples.", "app has " + JSON.stringify(target.state.text));
          assert(got.length === 0, "captured " + says(got));
          return "app now ends with " + JSON.stringify(target.state.text.slice(-24));
        });
      }
    } finally {
      try { target.proc.kill(); } catch (_) { /* already closed */ }
    }
  }

  /* ---- Phase 5: settings, expiry, ignoring a program, turning it off (these empty the history: last) ---- */
  if (monitor) {
    const { clipboard } = require("electron");
    const store = desktop.getClipStore();
    const panel = desktop.getPanelWin();

    await check("Settings → الحافظة: defaults, encryption line, and changes reach the app", async () => {
      openOptions("#clipboard"); // the window is already open: only the hash changes (no new load)
      const win = getOptionsWin();
      for (let i = 0; i < 50 && !(await js(win, `!!document.getElementById("clipPanel")`).catch(() => false)); i++) await wait(100);
      await wait(300);
      const r = await js(win, `({
        panel: !!document.getElementById("clipPanel"),
        enabled: document.getElementById("clipboardEnabled").checked,
        max: document.getElementById("clipboardMaxItems").value,
        expiry: document.getElementById("clipboardExpiryDays").value,
        cards: document.getElementById("clipboardSkipCards").checked,
        apps: [...document.querySelectorAll("#cbApps li span")].map(s => s.textContent),
        enc: document.getElementById("cbEnc").textContent,
        order: [...document.querySelectorAll(".panel")].findIndex(p => p.id === "clipPanel") < [...document.querySelectorAll(".panel")].findIndex(p => p.id === "review")
      })`);
      assert(r.panel && r.order, "section missing or misplaced");
      assert(r.enabled && r.max === "500" && r.expiry === "30" && r.cards, JSON.stringify(r));
      assert(r.apps.includes("keepassxc.exe") && r.apps.includes("bitwarden.exe"), "apps: " + r.apps);
      assert(r.enc.includes("مشفّر"), "encryption line: " + r.enc);
      await js(win, `(() => { const s = document.getElementById("clipboardMaxItems"); s.value = "250"; s.dispatchEvent(new Event("change")); return true; })()`);
      await wait(200);
      assert(store.maxItems === 250, "maxItems " + store.maxItems);
      await stores.local.set({ clipboardMaxItems: 500 });
      return r.enc;
    });

    await check("Settings → التحديثات: current version, automatic updates on by default, the switch is saved", async () => {
      const win = getOptionsWin();
      const r = await js(win, `({
        panel: !!document.getElementById("updatesPanel"),
        version: document.getElementById("upVersion").textContent,
        auto: document.getElementById("updatesAuto").checked,
        button: document.getElementById("upCheck").textContent
      })`);
      assert(r.panel && r.version.includes(app.getVersion()) && r.auto && r.button === "التحقق الآن", JSON.stringify(r));
      await js(win, `document.getElementById("updatesAuto").click(); true`);
      await wait(200);
      const off = (await stores.local.get({ updatesAuto: true })).updatesAuto;
      await stores.local.set({ updatesAuto: true });
      assert(off === false, "switch not saved");
      assert(desktop.updater.state.status === "idle", "the self-test must not contact GitHub: " + desktop.updater.state.status);
      return r.version;
    });

    await check("expiry (mocked clock): old unpinned clips deleted, pinned kept; runs hourly and at startup", async () => {
      const before = store.list({ limit: 1000 });
      const pinned = before.items.filter(i => i.pinned).length;
      const removed = desktop.runClipExpiry(Date.now() + 31 * 86400e3);
      const after = store.list({ limit: 1000 });
      assert(removed === before.total - pinned && after.total === pinned && pinned >= 1, `removed ${removed} of ${before.total}, pinned ${pinned}, left ${after.total}`);
      await stores.local.set({ clipboardExpiryDays: 0 });
      await capturesDuring(() => clipboard.writeText("lamha-never-expires"));
      assert(desktop.runClipExpiry(Date.now() + 3650 * 86400e3) === 0, "«أبدًا» still deleted");
      await stores.local.set({ clipboardExpiryDays: 30 });
      return `${removed} deleted, ${pinned} pinned kept`;
    });

    await check("tab: تجاهل هذا البرنامج excludes it and offers to delete its clips", async () => {
      monitor.sourceAppOverride = "wordpad.exe";
      await capturesDuring(() => clipboard.writeText("lamha from wordpad"));
      monitor.sourceAppOverride = null;
      const r = await js(mainWin, `(async () => {
        document.getElementById("tabCb").click();
        await new Promise(r => setTimeout(r, 300));
        const s = document.querySelector("#cbPane .lc-search");
        s.value = "lamha from wordpad"; s.dispatchEvent(new Event("input"));
        await new Promise(r => setTimeout(r, 250));
        if (!document.querySelector("#cbPane .cb-detail").hidden) document.querySelector("#cbPane .cb-dhead .link").click();
        await new Promise(r => setTimeout(r, 200));
        document.querySelector("#cbPane .lc-row").click();
        await new Promise(r => setTimeout(r, 300));
        document.querySelector("#cbPane .cb-ignore").click();
        await new Promise(r => setTimeout(r, 400));
        const dlg = document.querySelector(".lc-dialog");
        const text = dlg ? dlg.textContent : "";
        if (dlg) dlg.querySelector("button").click(); // حذف
        await new Promise(r => setTimeout(r, 400));
        return text;
      })()`);
      assert(r.includes("wordpad") && r.includes("حذف"), "dialog: " + r);
      const { clipboardExcludedApps } = await stores.local.get("clipboardExcludedApps");
      assert(clipboardExcludedApps.includes("wordpad.exe"), "not excluded");
      assert(!store.apps().some(a => a.app === "wordpad.exe"), "its clips are still there");
      await js(mainWin, `lamhaClipboard.includeApp("wordpad.exe")`);
    });

    await check("tab: مسح غير المثبّت asks, clears everything unpinned in one step, keeps pinned items", async () => {
      for (const t of ["lamha-clear-1", "lamha-clear-2", "lamha-clear-3"]) await capturesDuring(() => clipboard.writeText(t), 400);
      const pinned = store.list({ filter: "pinned" }).total;
      const unpinned = store.list().total - pinned;
      assert(pinned >= 1 && unpinned >= 3, `pinned ${pinned}, unpinned ${unpinned}`);
      const r = await js(mainWin, `(async () => {
        document.getElementById("tabTr").click();
        document.getElementById("tabCb").click();
        await new Promise(r => setTimeout(r, 400));
        const btn = document.getElementById("cbClear");
        const label = btn.textContent, disabledBefore = btn.disabled;
        btn.click();
        await new Promise(r => setTimeout(r, 300));
        const dlg = document.querySelector(".lc-dialog");
        const question = dlg ? dlg.textContent : "";
        if (dlg) dlg.querySelector("button").click(); // مسح
        await new Promise(r => setTimeout(r, 500));
        return { label, disabledBefore, question, disabledAfter: btn.disabled, toast: (document.querySelector("#cbPane .lc-toast") || {}).textContent || "" };
      })()`);
      assert(!r.disabledBefore && r.label.includes("مسح غير المثبّت"), JSON.stringify(r));
      assert(r.question.includes("تبقى العناصر المثبّتة"), "question: " + r.question);
      assert(store.list().total === pinned && store.list({ filter: "pinned" }).total === pinned, "left: " + store.list().total);
      assert(r.disabledAfter, "button still active with nothing to clear");
      assert(r.toast.includes("حُذف"), "toast: " + r.toast);
      return `${unpinned} cleared, ${pinned} pinned kept — ${r.label}`;
    });

    await check("turning the feature off with «حذف» empties the history on disk", async () => {
      const win = getOptionsWin();
      assert(store.list().total > 0, "nothing to delete");
      const text = await js(win, `(async () => {
        const sw = document.getElementById("clipboardEnabled");
        sw.click(); // off → the question
        await new Promise(r => setTimeout(r, 300));
        const dlg = document.querySelector(".lc-dialog");
        const t = dlg ? dlg.textContent : "";
        if (dlg) dlg.querySelector("button").click(); // حذف
        await new Promise(r => setTimeout(r, 500));
        return t;
      })()`);
      assert(text.includes("هل تريد حذف السجل الحالي أيضًا؟") && text.includes("الإبقاء عليه"), "question: " + text);
      assert(!monitor.enabled, "still enabled");
      assert(store.list().total === 0, "history not emptied");
      const onDisk = new (require("../clipboard-store").ClipboardStore)({ file: store.file, normalize: globalThis.LamhaArabic.normalizeForSearch, safeStorage: require("electron").safeStorage });
      assert(onDisk.list().total === 0, "file still has clips");
      const off = await capturesDuring(() => clipboard.writeText("lamha-after-off"));
      assert(off.length === 0, "recorded while off");
      await stores.local.set({ clipboardEnabled: true }); // turned on elsewhere (the panel's opt-in card): the switch follows
      await wait(400);
      const shown = await js(win, `document.getElementById("clipboardEnabled").checked`);
      await stores.local.set({ clipboardEnabled: false });
      assert(shown === true, "the settings switch did not follow the change");
    });

    await check("English interface: main window, الحافظة tab, quick panel and settings switch at once, left to right", async () => {
      /** Polls `code` in `win` until it returns true (pages reload themselves when the language changes). */
      const until = async (win, code, what) => {
        for (let i = 0; i < 60; i++) {
          if (await js(win, code).catch(() => false)) return;
          await wait(100);
        }
        throw new Error("timed out waiting for " + what);
      };
      await stores.local.set({ clipboardEnabled: true });
      await stores.sync.set({ uiLang: "en" });
      await until(mainWin, `document.documentElement.dir === "ltr" && (document.getElementById("tabCb") || {}).textContent === "Clipboard"`, "the main window in English");
      const main = await js(mainWin, `({ tabs: [...document.querySelectorAll(".tabs button")].map(b => b.textContent.replace(/\\s*[0-9]+$/, "").trim()), settings: document.getElementById("openOptions").textContent })`);
      assert(JSON.stringify(main.tabs) === JSON.stringify(["Translate", "Write ✨", "Review", "Clipboard"]) && main.settings === "Settings", JSON.stringify(main));

      const win = getOptionsWin();
      await until(win, `document.documentElement.dir === "ltr" && !!document.getElementById("clipPanel") && document.querySelector("#clipPanel h2").textContent === "Clipboard 📋"`, "settings in English");
      const updates = await js(win, `document.querySelector("#updatesPanel h2").textContent`);
      assert(updates === "Updates", "updates section: " + updates);

      await desktop.panelReady();
      await desktop.openPanel({ target: 0 });
      await until(panel, `document.documentElement.dir === "ltr" && (document.querySelector(".cp-head strong") || {}).textContent === "Clipboard" && !!document.querySelector("#cpBody .lc-search")`, "the quick panel in English");
      const ph = await js(panel, `document.querySelector("#cpBody .lc-search").placeholder`);
      desktop.hidePanel();
      assert(ph === "Search the clipboard…", "placeholder " + ph);
      assert(/^Lamha( — [0-9]+ to review)?$/.test(desktop.trayTooltip()), "tray: " + desktop.trayTooltip());

      await stores.sync.set({ uiLang: "ar" });
      await until(mainWin, `document.documentElement.dir === "rtl" && (document.getElementById("tabCb") || {}).textContent === "الحافظة"`, "the main window back in Arabic");
      assert(/^لمحة( — [٠-٩]+ للمراجعة)?$/.test(desktop.trayTooltip()), "tray: " + desktop.trayTooltip());
      return main.tabs.join(" · ");
    });
  }

  // optional screenshots for a visual check: $env:LAMHA_SHOTS = "C:\some\folder"
  if (process.env.LAMHA_SHOTS) {
    if (process.env.LAMHA_SMOKE_LANG) { // pictures in that language (the checks above switch it back and forth)
      await stores.sync.set({ uiLang: process.env.LAMHA_SMOKE_LANG });
      await wait(2000);
    }
    const shot = async (win, name) => {
      win.show();
      await wait(700);
      fs.writeFileSync(path.join(process.env.LAMHA_SHOTS, name), (await win.webContents.capturePage()).toPNG());
    };
    fs.mkdirSync(process.env.LAMHA_SHOTS, { recursive: true });
    for (const [tab, name] of [["tabTr", "1-translate.png"], ["tabWr", "2-write.png"], ["tabRv", "3-review.png"]]) {
      await js(mainWin, `document.getElementById("${tab}").click(); true`);
      if (tab === "tabWr") await js(mainWin, `(() => { const d = document.getElementById("draft"); d.value = "She dont like apples and she go to school yesterday."; d.dispatchEvent(new Event("input")); document.querySelector("#wrTools button").click(); return true; })()`);
      if (tab === "tabWr") await wait(4000);
      if (tab === "tabRv") await js(mainWin, `(() => { document.dispatchEvent(new KeyboardEvent("keydown", { key: " " })); return true; })()`);
      await shot(mainWin, name);
    }
    await shot(getOptionsWin(), "4-settings.png");
    if (desktop && desktop.clipboardMonitor) {
      await js(getOptionsWin(), `document.getElementById("clipPanel").scrollIntoView({ block: "start" }); true`);
      await shot(getOptionsWin(), "10-settings-clipboard.png");
    }
    if (desktop && desktop.clipboardMonitor) {
      // the last checks turned the history off and emptied it: some sample clips for the pictures
      const { clipboard } = require("electron");
      await stores.local.set({ clipboardEnabled: true });
      for (const t of ["She dont like apples.", "سأرسل لك التقرير النهائي اليوم", "Keep your promise."]) { await clipboard.writeText(t); await wait(400); }
      await js(mainWin, `document.getElementById("tabCb").click(); true`);
      await shot(mainWin, "6-clipboard-tab.png");
      await js(mainWin, `(async () => {
        const s = document.querySelector("#cbPane .lc-search"); s.value = "keep your"; s.dispatchEvent(new Event("input"));
        await new Promise(r => setTimeout(r, 250));
        document.querySelector("#cbPane .lc-row").click();
        await new Promise(r => setTimeout(r, 300));
        document.querySelector('#cbPane .ca-item[data-act="translate"]').click();
        return true;
      })()`);
      await shot(mainWin, "8-clipboard-detail-tools.png");
      await desktop.openPanel({ target: 0 });
      await js(desktop.getPanelWin(), `(() => { const s = document.querySelector("#cpBody .lc-search"); s.value = "lamha"; s.dispatchEvent(new Event("input")); return true; })()`);
      await wait(500);
      fs.writeFileSync(path.join(process.env.LAMHA_SHOTS, "7-quick-panel.png"), (await desktop.getPanelWin().webContents.capturePage()).toPNG());
      await js(desktop.getPanelWin(), `(async () => {
        const s = document.querySelector("#cpBody .lc-search"); s.value = "She dont"; s.dispatchEvent(new Event("input"));
        await new Promise(r => setTimeout(r, 250));
        s.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
        await new Promise(r => setTimeout(r, 300));
        document.querySelector('#cpBody .ca-item[data-act="proofread"]').click();
        return true;
      })()`);
      await wait(700);
      fs.writeFileSync(path.join(process.env.LAMHA_SHOTS, "9-panel-proofread.png"), (await desktop.getPanelWin().webContents.capturePage()).toPNG());
      desktop.hidePanel();
    }
    console.log("screenshots →", process.env.LAMHA_SHOTS);
  }

  for (const f of smokeCleanups) f();
  const failed = results.filter(r => r.startsWith("  ✗")).length;
  console.log(`\n${results.length - failed}/${results.length} passed` + (results.some(r => r.startsWith("  !")) ? " (! = optional, needs network/Ollama)" : ""));
  const profile = app.getPath("userData");
  app.exit(failed ? 1 : 0);
  // Chromium keeps the profile open until it has exited; clean up the temporary folder best-effort
  try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); } catch (_) { /* Windows cleans %TEMP% */ }
};
