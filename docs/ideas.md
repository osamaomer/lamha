# Lamha: ideas for later

Plans and thoughts the user hasn't decided on yet. Moved out of CLAUDE.md on 2026-10-01; CLAUDE.md → Open items points here.

## The Windows app on Linux (planned 2026-09-29; not started, the user will decide when)

The user asked whether the app can run on Linux and agreed this plan. **Left out on purpose:** the double-click Write button and **Insert** (the card's Insert / Replace, the clipboard panel's Enter, and the Write button's own insert all focus the other program and press Ctrl+V). On Linux those copy instead and say "Copied. Press Ctrl+V". Insert could come back on X11 later with `xdotool`.

**Why it's uneven:** X11 lets an app read other apps' selection, place windows and send keys, like Windows. Wayland (the default on Ubuntu and Fedora) blocks most of that; GNOME is the strictest, KDE allows more.

| Feature | X11 | Wayland GNOME | Wayland KDE |
|---|---|---|---|
| Main window, lookups, review, Settings, Wikipedia (reader, downloads), packs, AI | ✅ | ✅ | ✅ |
| Shortcut card for selected text | ✅ | ⚠️ test | ⚠️ test |
| Card next to the mouse | ✅ | ❌ the desktop places it | ❌ |
| Clipboard history | ✅ | ❌ not allowed | ✅ with `wl-clipboard` |
| Firefox ↔ app link | ✅ | ✅ | ✅ |
| Automatic updates | AppImage only | AppImage only | AppImage only |

**Support X11? Yes, as "works, not the focus"** (the user asked what dropping it would cost):
- It saves little: Electron picks X11 or Wayland by itself, and on X11 the selection (same PRIMARY call, no focus needed), `globalShortcut` and the card next to the mouse (`cardPlacement` unchanged) need no extra code. X11-only code is the clipboard check every ~0.5 s (~20–30 lines) and, optionally, which program copied (~30 lines): about a day.
- Dropping it would lose users: GNOME 49 / Ubuntu 25.10, Fedora and (announced) KDE Plasma 6.8 leave X11, but **Linux Mint Cinnamon** (the usual first Linux after Windows, likely Lamha's audience; its Wayland is experimental), Xfce, MATE, light distros, Ubuntu 22.04 / 24.04 LTS on the X11 session and some NVIDIA setups still use it. It would also lose X11's better experience (card by the mouse, clipboard history, excluded programs), the easy CI test (`xvfb` is X11; headless Sway / Weston is more work), and Insert on X11 later.
- So: write Wayland-first, add only the small X11 clipboard check, let CI test on `xvfb`, test by hand on Ubuntu GNOME Wayland and on Mint now and then.

**Order:** 1 → 2 → 5 → 4 → 3. After 1, 2 and 5 there's a usable Linux release; the Firefox link and clipboard history can follow in an update.

1. **The app starts with its core features (small).**
   - `app-rules.js`: `featuresFor(platform, session)` → e.g. `{ writeButton, insert, clipboard, placeNearMouse }`, tested in test-desktop. main.js already loads `native.js`, `selection.js`, `uia-context.js` and the clipboard monitor only on Windows; every use of them asks `featuresFor()` instead.
   - Settings and the card: a `linux` class (like `desktop`) hides the Write button setting and Windows-only rows; Insert / Replace become Copy; new strings in `i18n-desktop.js`.
   - Start with the computer: `setLoginItemSettings` doesn't work on Linux → the tray checkbox writes / deletes `~/.config/autostart/lamha.desktop`.
   - API keys: `safeStorage` needs a keyring (GNOME / KDE). When `safeStorage.getSelectedStorageBackend()` is `basic_text`, treat keys as unencrypted and say so in Settings.
   - `clipboard-privacy.js`: Linux program names have no `.exe` (`keepassxc`).
2. **The shortcut card (medium, the most important).**
   - Selection: `clipboard.readText("selection")` (Linux's PRIMARY selection): no Ctrl+C sent, the user's clipboard untouched. On Wayland it's only readable while a Lamha window has the focus, so the card opens and takes the focus first, then reads it: test on GNOME and KDE.
   - Shortcut: X11 `globalShortcut` as now; Wayland through Electron's global shortcuts portal (recent KDE and GNOME; the desktop asks the user once). Always-working fallback: `lamha --lookup`, bound by the user in the desktop's keyboard settings (Settings explains how), received through `second-instance`.
   - Placement: X11 next to the mouse (`cardPlacement`); Wayland can't place windows or read the mouse (`getCursorScreenPoint()` isn't reliable), so the desktop decides, usually the centre.
   - The sentence around the word (Windows: `uiaContext.around`): Linux's AT-SPI could do it, but leave it out at first.
3. **Clipboard history (medium).**
   - X11 (the only X11-specific code in the plan): check for a change every ~0.5 s (or `XFixes`); the program that copied from `_NET_WM_PID` → `/proc/<pid>/comm`, so excluded programs keep working.
   - Wayland KDE / Sway: `wl-paste --watch` (Settings asks to install `wl-clipboard` when missing). GNOME Wayland: not possible; Settings says it isn't available on this desktop.
   - Skip clips marked `x-kde-passwordManagerHint` (KeePassXC and others) everywhere: on Wayland the source program is unknown.
   - The panel's Enter copies instead of pasting.
4. **The Firefox link (small):** same messages (`hello`, `wikiSummary`, `openReader`, `deckSync`), new transport in native-bridge.js: the host manifest as a file in `~/.mozilla/native-messaging-hosts/com.artworklab.lamha.json` (no registry); `host.py` (Python 3 is installed on Ubuntu, Fedora, Mint) instead of host.bat / host.ps1; a Unix socket in `$XDG_RUNTIME_DIR` instead of the named pipe (only the user can open it, so the audit's other-user risk is gone); the token check stays. Firefox as a Snap (Ubuntu's default) or Flatpak reaches native hosts through a special permission: test it, and say so in Settings when it doesn't work.
5. **Packages and releases (small).** `package.json` `linux` target: AppImage (updates itself through `latest-linux.yml`, separate from Windows' `latest.yml`) and `.deb` (can't update itself: Settings → Updates shows a download link, like Portable); maybe `.rpm` later. Recent Ubuntu lacks `libfuse2`, which AppImage needs (README). release.yml gets a second job on `ubuntu-latest` adding its files to the same release; checks.yml runs `npm test` on Ubuntu too.
6. **Testing.** CI: a Linux self-test on `xvfb` (X11): start, Settings, a lookup, the reader, the card through a real shortcut and selection. By hand: Ubuntu GNOME (Wayland), Kubuntu KDE (Wayland), one X11 session (a spare PC or a virtual machine). WSLg on the work PC only for quick checks.
- Documents: a README Linux section (the table, installing, `libfuse2`, the `lamha --lookup` shortcut), CLAUDE.md platform rules, a changelog entry.

**Decisions for the user before starting:** which Linux officially (suggested: Ubuntu GNOME first, KDE second, others "should work"); AppImage + `.deb` or AppImage only; clipboard history missing on GNOME, acceptable?; Insert on X11 later?

## Earning from Lamha (the user's thoughts, 2026-09-29; nothing decided)

- **Allowed:** the code is MIT, the data CC BY-SA 4.0 (commercial use fine with the credits kept, see README → License). MIT also lets anyone copy, sell or rebrand it, so what's sold is convenience, a service or the name, not the code.
- **Settle first:** translation uses Google's free unofficial endpoint (`translate.googleapis.com/translate_a/…?client=gtx`, background.js `PROVIDERS`), which Google may limit or block. A paid version should use the official Cloud Translation API (~$20 per million characters, priced in), or rely on the AI translator, or accept the risk knowingly.
- **Options, least to most work:**
  1. **Donations:** a "Support Lamha" link in Settings, the README and the Firefox add-on page (AMO's contribute button); GitHub Sponsors, Ko-fi or Buy Me a Coffee (check which pay out in the user's country). Small amounts, but a measure of interest.
  2. **A paid Microsoft Store listing** (or pay what you want): the code stays free on GitHub, the Store build is the one-click install with updates. The Store signs the app, which also settles code signing (#5 of the 1.9.6 list, the SmartScreen warning) without the ~$10/month signing service.
  3. **Lamha Plus, AI without API keys:** a subscription where the writing tools and AI translation just work. Needs a small server passing requests to the AI with Lamha's own key, accounts, payments (Paddle or Lemon Squeezy handle taxes), usage limits, and a privacy policy (user text passes through the server). AI costs grow per user, so the price must cover them. The likeliest real income, and the most work.
  4. **Schools and English-teaching centres:** a licence with support, e.g. a word deck shared by a class. Mostly sales work.
  5. **Grants** for open-source and language-access projects; an Arabic-first learning tool fits.
- **Suggested order:** now, a donation link and claiming the name "Lamha" (trademark) and a domain; with code signing, the Microsoft Store listing; if donations and sales show interest, Lamha Plus, after settling the Google question.
- Not legal or tax advice: check trademarks, taxes and selling across countries where the user lives.

## Less dependence on Google's unofficial endpoint (the user's question, 2026-09-29; nothing decided)

Losing Google would hit sentences, pages and word-in-context; single words come from the offline dictionary (Google only adds online dictionary detail). With no AI set up, a blocked Google means no sentence translation today. Prices below are from memory: check before deciding.
1. **Keep it, as one option among several:** it has worked for years and Lamha already falls back (dictionary, packs, AI). Fine for the free version, especially with 3 as the fallback.
2. **Official services** (need a key, so they fit Lamha Plus, with the key on Lamha's server):

   | Service | Free per month | After that | Arabic |
   |---|---|---|---|
   | Microsoft Translator | ~2M characters | ~$10 / million | good |
   | Google Cloud Translation | ~500k | ~$20 / million | today's quality |
   | DeepL API | ~500k | higher, plus a monthly fee | newer |
   | Amazon Translate | 2M, first year only | ~$15 / million | yes |

   They return only the translation: the parts of speech and reverse translations from `translate_a` are lost (the offline dictionary covers most). Google's voice is unofficial too; the official one is Cloud Text-to-Speech, and the Windows voices already back it up.
3. **A local translator on the user's PC:**

   | Engine | Size per pair | Speed | Licence | Notes |
   |---|---|---|---|---|
   | **Bergamot** (Mozilla, Firefox's own translation) | ~20–40 MB | fast, hundreds of words/s on a CPU | open (check the models') | WebAssembly: Firefox and the app alike |
   | Opus-MT (Helsinki) | ~75 MB compressed | good | CC BY 4.0 | many pairs, Arabic included |
   | NLLB (Meta) | ~600 MB+ | slower | **non-commercial** | rules out selling Lamha |
   | Ollama (already built) | 2–5 GB | slow without a GPU | per model | a big install for ordinary users |

   - **Bergamot fits best:** "translation packs" downloaded from Settings like the language packs (`packs.js`, `pack-store.js`, GitHub release hosting); offline and free per use; finishes the "works with no internet" goal without Ollama.
   - **Costs:** no money ongoing. Work: medium to large, a few weeks: the WebAssembly engine (~5–8 MB) in a worker, model packs, sentence splitting and batching for pages, `wasm-unsafe-eval` in the extension's CSP; ~100–200 MB more memory while translating.
   - **Limits:** quality below Google (Arabic idioms, informal text: the gist, not polish); one model per direction, so French → Arabic may pivot through English (errors add up).
- **Suggested order:** (1) measure first: add Bergamot and Opus-MT to `tools/compare-translation.mjs` (Google, Gemini, Ollama already there) for real Arabic quality; (2) if acceptable, translation packs as the offline fallback: Google → local → AI; (3) for a paid version, Microsoft Translator (cheapest, generous free tier) or Google Cloud through Lamha's server, the local translator still the offline fallback.
- **Related (the user asked):** why API keys and not the chat websites (Gemini, ChatGPT): driving them breaks their terms, breaks at every redesign or CAPTCHA, needs the user's logged-in session (their whole account), gives no fixed answer shape (Lamha's JSON, the foreign-letter check, a model per task), fills their chat history, and chatbot plans don't include API use. Gemini's key is free within limits and Ollama needs none. A legitimate middle way: an "Open in Gemini / ChatGPT" button that copies a ready prompt (the answer doesn't come back into Lamha).
