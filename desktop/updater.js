/* Lamha desktop — updates from the GitHub Releases of osamaomer/lamha.
 * Installed app (Setup): electron-updater checks after startup and every 6 hours, downloads in the background,
 * and installs on the next restart or quit; "أعد التشغيل الآن" does it at once.
 * Portable: it can't replace itself, so it only says a new version exists and links to the release page.
 * The update source and the network are passed in, so the self-test can use fakes. */
"use strict";

const RELEASES = "https://github.com/osamaomer/lamha/releases";
const LATEST_API = "https://api.github.com/repos/osamaomer/lamha/releases/latest";
const FIRST_CHECK_MS = 15e3;
const EVERY_MS = 6 * 3600e3;

/** 1.10.0 > 1.9.2; "v1.5.0" is fine; pre-release suffixes are ignored. */
function newer(a, b) {
  const parts = v => String(v || "").replace(/^v/, "").split("-")[0].split(".").map(n => parseInt(n, 10) || 0);
  const [x, y] = [parts(a), parts(b)];
  for (let i = 0; i < 3; i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0);
  return false;
}

/**
 * @param {object} o
 * @param {string} o.version            this app's version
 * @param {boolean} o.packaged          false while developing: no checks
 * @param {boolean} o.portable          the Portable build (process.env.PORTABLE_EXECUTABLE_DIR)
 * @param {() => object} o.autoUpdater  electron-updater's autoUpdater (loaded lazily)
 * @param {(url: string) => Promise<object>} o.fetchJson
 * @param {(title: string, body: string, onClick?: () => void) => void} o.notify
 * @param {() => void} o.onChange       state changed (tray menu, settings)
 * @param {(url: string) => void} o.openUrl
 */
function createUpdater({ version, packaged, portable, autoUpdater, fetchJson, notify, onChange, openUrl }) {
  const state = { status: "idle", version: null, error: null, checkedAt: 0 }; // idle | checking | available | downloading | ready | latest | error | dev
  let au = null, timer = null, manual = false;
  const set = patch => { Object.assign(state, patch); onChange(); };

  function updater() {
    if (au) return au;
    au = autoUpdater();
    au.autoDownload = true;
    au.autoInstallOnAppQuit = true;
    au.logger = null; // nothing about the user's files in logs
    au.on("checking-for-update", () => set({ status: "checking", error: null }));
    au.on("update-not-available", () => {
      set({ status: "latest", checkedAt: Date.now() });
      if (manual) notify("لديك أحدث إصدار", `الإصدار ${version} هو الأحدث.`);
    });
    au.on("update-available", info => set({ status: "downloading", version: info.version, checkedAt: Date.now() }));
    au.on("update-downloaded", info => {
      set({ status: "ready", version: info.version });
      notify("تحديث جديد جاهز", `الإصدار ${info.version} — أعد تشغيل لمحة لتثبيته، أو سيُثبَّت عند الخروج. اضغط هنا لإعادة التشغيل الآن.`, restart);
    });
    au.on("error", err => {
      set({ status: "error", error: String((err && err.message) || err).slice(0, 200) });
      if (manual) notify("تعذّر التحقق من التحديثات", "تحقق من اتصالك بالإنترنت ثم حاول مجددًا.");
    });
    return au;
  }

  /** Portable: the latest release on GitHub; if newer, a notice that opens the release page. */
  async function checkPortable() {
    set({ status: "checking", error: null });
    try {
      const rel = await fetchJson(LATEST_API);
      const latest = String(rel.tag_name || "").replace(/^v/, "");
      if (latest && newer(latest, version)) {
        set({ status: "available", version: latest, checkedAt: Date.now() });
        notify("إصدار جديد من لمحة", `الإصدار ${latest} متاح. نسخة Portable لا تُحدّث نفسها — اضغط لتنزيله.`, () => openUrl(rel.html_url || RELEASES));
      } else {
        set({ status: "latest", checkedAt: Date.now() });
        if (manual) notify("لديك أحدث إصدار", `الإصدار ${version} هو الأحدث.`);
      }
    } catch (err) {
      set({ status: "error", error: String((err && err.message) || err).slice(0, 200) });
      if (manual) notify("تعذّر التحقق من التحديثات", "تحقق من اتصالك بالإنترنت ثم حاول مجددًا.");
    }
  }

  /** `byUser`: from the tray or Settings, so "up to date" and errors are reported too. */
  async function check(byUser = false) {
    if (!packaged) { set({ status: "dev" }); if (byUser) notify("التحديثات", "التحديث التلقائي يعمل في النسخة المثبّتة فقط."); return state; }
    if (["checking", "downloading"].includes(state.status)) return state;
    if (state.status === "ready") { if (byUser) restart(); return state; }
    manual = byUser;
    if (portable) await checkPortable();
    else await updater().checkForUpdates().catch(() => { /* reported through the "error" event */ });
    return state;
  }

  function restart() {
    if (state.status === "ready" && au) au.quitAndInstall(true, true); // silent (per-user install), then Lamha starts again
    else if (state.status === "available") openUrl(RELEASES + "/latest");
  }

  /** Automatic checks on (setting «التحديثات التلقائية») or off. */
  function schedule(on) {
    clearTimeout(timer);
    clearInterval(timer);
    timer = null;
    if (!on || !packaged) return;
    timer = setTimeout(function tick() {
      check(false);
      timer = setInterval(() => check(false), EVERY_MS);
      timer.unref();
    }, FIRST_CHECK_MS);
    timer.unref();
  }

  return { check, restart, schedule, state, releasesUrl: RELEASES };
}

module.exports = { createUpdater, newer };
