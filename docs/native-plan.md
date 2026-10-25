# Lamha 3: the native Windows app

The plan for rebuilding Lamha as a native Windows 11 app (WinUI 3 + Windows App SDK) and dropping the Firefox extension and the Linux plan. Written 2026-10-24.

**Where it stands:** phases 0 and 1 are done ([phase 0](#phase-0-results), [phase 1](#phase-1-progress)). Phase 2 (the brain) is built but for the parity tool ([progress](#phase-2-progress)), and phase 3 (Windows integration) is built ([progress](#phase-3-progress)). Phase 4 (the UI) is under way: the card is done and out as the first beta; the main window, Settings and the clipboard panel are built ([progress](#phase-4-progress)). No new beta until Lamha can update itself (phase 5).

**Branches:** the phases were built on `native/phase-0` … `native/phase-4`, one line of work in the order 0, 2, 3, 1, 4. On 2026-10-25 it was merged into `main` (decision 0003: `main` is Lamha 3) and those branches were deleted; the progress sections below still name the branch each phase was built on. New work goes on short `lamha3/<what>` branches, merged into `main`.

Each section ends with **Ideas** (suggestions, not decided) and **Questions** (each with a recommendation and the phase it must be answered by). The [last section](#questions-by-phase) collects the questions by phase.

## Decisions

| Question | Decision |
|---|---|
| Platform | **Windows 11 only.** No Firefox extension, no Linux, no Windows 10. |
| UI framework | **WinUI 3** on the Windows App SDK, written in **C# / .NET 10 (LTS)**. |
| Design | **Fluent, with Lamha's identity:** Mica, NavigationView and Fluent controls and motion, plus Lamha's colours, type and the floating card's character. |
| AI providers | **Claude, Gemini, OpenAI, Ollama**, and a **custom server** for anything that speaks OpenAI's API (LM Studio, OpenRouter, Groq, Azure OpenAI…). |
| Wikipedia | The **online summary on the card stays** (plain text, no web view). **Offline Wikipedia and the reader wait** for a [future plan](#future-offline-wikipedia-and-the-reader). |
| Install and updates | **A normal installer with automatic updates from GitHub Releases** (Velopack), like today. Code signing is advised (Azure Trusted Signing, ~$10/month) but doesn't block anything. |
| The Electron app meanwhile | **Bug fixes only**, from a `release/2.x` branch. Native becomes **Lamha 3.0** once it does everything in scope, and it imports users' data. |
| Goals, in order | 1. A clean, professional codebase. 2. UI and UX. 3. Memory that stays low as features are added. |

Why C# rather than C++/WinRT: it's the language WinUI 3 is mainly documented and supported in, it has MVVM source generators, and NativeAOT gives near-native startup and memory. C++ would cost much more time for little gain.

## What changes for users

**Kept, everything else the Windows app does today:** word lookup and sentence translation over any program (Alt+Shift+L), writing tools (Alt+Shift+W) and Write new, Google / AI / automatic translation, explaining words in their own language, the offline dictionary and the downloadable language packs, the review deck and Today (goal, streak, word of the day), history, the mistake journal, clipboard history (Alt+Shift+V), the Wikipedia summary on the card, Arabic and English interfaces, themes and motion levels, backups, Report a problem, and automatic updates.

**New:** OpenAI and custom OpenAI-compatible servers as AI providers.

**Dropped:**
- The Firefox extension, with everything that only exists there: page translation, the double-click Write button, the pill on web pages, per-site switches (`disabledSites`, `showInInputs`, `writeOnDblClick`, `trPages`).
- The Firefox ↔ app link: native messaging, `host.bat` / `host.ps1`, the named pipe, and the deck sync between them.
- The Linux plan in [ideas.md](ideas.md).
- Windows 10.

**On hold:** offline Wikipedia (downloaded `.zim` files) and the reader window. Users' downloaded files **stay on disk**, untouched, so they work again when the feature returns.

**Better:**
- Memory, startup, and how Lamha feels next to other Windows 11 apps.
- Offline speech through Windows' own Arabic and English voices, when Google's can't be reached.
- No Node, Chromium or PowerShell running in the background, which antivirus programs trust more.

**Questions**
- **Tell 2.x users with downloaded Wikipedias before they update?** Recommendation: yes. The bridge update says offline Wikipedia pauses in 3.0, and those users can choose to stay on 2.x until it's back. *(phase 6)*

## Architecture

### Projects

The app is split into layers. Each layer may only use the layers below it. Everything that isn't UI or Windows-specific lives in `Lamha.Core`, so it can be tested without a window.

```
Lamha.App        WinUI 3: windows, pages, view models, controls, styles, tray, startup
   │
   ├── Lamha.Windows   Win32 and UI Automation: hotkeys, reading the selection, Replace/Insert,
   │                   clipboard monitor, the sentence around a word, DPAPI, foreground window
   ├── Lamha.Data      SQLite: settings, history, deck, mistakes, clipboard, dictionary readers,
   │                   schema migrations, importing Lamha 2's files
   └── Lamha.Core      the brain, platform-free: translation service, AI providers, writing tools,
                       review scheduling, Today, the Wikipedia summary, speech text, diagnostics.
                       Talks to storage and Windows only through interfaces.
```

`background.js` maps mostly to `Lamha.Core`, `desktop/*.js` to `Lamha.Windows` and `Lamha.Data`, and the pages (`popup/`, `options/`, `content/`, `desktop/renderer/`) to `Lamha.App`.

### Main technical choices

| Area | Choice | Why |
|---|---|---|
| UI pattern | MVVM with **CommunityToolkit.Mvvm** (source-generated) | No reflection, so it works with NativeAOT. Keeps UI and logic apart. |
| Composition | `Microsoft.Extensions.DependencyInjection` + `Hosting` | One place where services are wired, plus logging and options. |
| Settings and per-device data | **SQLite** (`Microsoft.Data.Sqlite`), one database in `%LOCALAPPDATA%\Lamha` | Replaces the JSON files that are read and rewritten whole. Queries instead of loading the whole deck into memory. |
| Offline dictionary | The 22 MB of JSON shards rebuilt as **one read-only SQLite file** by the dictionary builder; packs ship as SQLite files too | Read from disk on demand: nothing stays in memory. |
| API keys | **Windows Credential Locker** (`PasswordVault`), or DPAPI | Never in the database, never in logs. |
| Win32 calls | **CsWin32** (source-generated P/Invoke) | Replaces koffi. Typed and checked when the app is built. |
| The sentence around a word | UI Automation **in-process**, on its own STA thread with a timeout | Replaces `lamha-uia.exe`. A program that hangs can't freeze Lamha's UI. |
| JSON | `System.Text.Json` with source generation | Works with NativeAOT. |
| HTTP | One `HttpClient` for everything, with server-sent-event streaming | Words appear as they arrive, as today. |
| Speech | `MediaPlayer` for Google's voice; `SpeechSynthesizer` (Windows' voices) as the offline fallback | |
| Logging | `Microsoft.Extensions.Logging` to a small rolling file in the data folder | Same rule as today: lengths and error names, never user text. |
| Updates | **Velopack**, publishing to GitHub Releases | Delta updates and a normal installer, as today. |
| Publishing | **NativeAOT + trimming**, x64 and ARM64 | Fastest start and lowest memory. |
| Tests | xUnit for Core, Data and Windows; FlaUI for UI smoke tests on a real desktop | |

No web view anywhere in 3.0. That alone removes the biggest memory risk.

### Memory: the rules that keep it low

1. **Nothing lives in the tray except the tray icon, the hotkeys and the clipboard listener.** Windows are created when they open and released when they close. The card window is the only exception, kept hidden so it can appear instantly, and only if measuring shows it's worth it.
2. **Data stays on disk:** dictionary, history, deck and clipboard are queried from SQLite, never loaded whole.
3. **Heavy parts load on demand:** language packs, AI provider clients.
4. **Budgets are tested, not hoped for.** `tools/Measure/measure.ps1` reads Task Manager's number (private working set) and commit (private bytes) from outside the process, and fails above either budget:

   | State | 2.0.1 (Task Manager · commit) | Phase 0 prototype | Budget (Task Manager / commit) |
   |---|---|---|---|
   | In the tray, just started | ~150 · ~320 MB | 29 · 46 MB | **35 / 55 MB** |
   | Card open after a lookup | ~165 · ~330 MB | 55 · 73 MB | **70 / 85 MB** |
   | Main window open | ~155 · ~313 MB | not built yet (its frame: 18 · 86 MB; with Translate: 26 · 92 MB) | **90 / 110 MB** (to confirm in phase 4) |
   | Back in the tray after use | ~165 · ~355 MB | ~0 after the idle trim · 73 MB | **45 / 85 MB** after the card; **45 / 95 MB** after the main window, provisional ([decision 0013](decisions/0013-main-window-memory.md)) |

   Measured in phase 0 ([decision 0008](decisions/0008-memory-budgets.md)).
5. **Idle memory goes back to Windows** 5 seconds after the last window closes (`Services/IdleMemory.cs`). That's why Task Manager shows almost nothing in the tray, and why commit has its own budget: it's the honest number.

**Ideas**
- **A memory line in Report a problem** (already there today): keep it, and add the current state (tray, card, window) so a report says *when* memory was high.
- **"Lamha uses N MB" in Settings → About** with the budget beside it, as a small honesty feature for users who chose Lamha for being light.

**Answered in phase 0**
- **Keep the card window, or create it on every lookup?** Keep it ([decision 0009](decisions/0009-card-window-kept.md)). Measured, closing it frees ~1 MB: the cost is the XAML stack starting up, not the window.

**Ideas from phase 0**
- **Measure the backdrop:** most of the ~27 MB the first card costs is XAML, composition and acrylic. Phase 1 compares acrylic, Mica and solid on memory as well as looks.
- **A smaller installer:** WinUI ships its own strings for ~100 languages (most of the 243 files). Lamha needs Arabic and English; trimming the rest could take several MB off the setup. Worth checking that nothing breaks.

## AI providers

One interface, `IAiProvider`: send a request (streaming or not), list models, test the connection. Writing tools and the AI translator only see this interface. That's how today's `aiProviderConfig()` + `aiComplete()` work too.

| Provider | Protocol | Key | Models |
|---|---|---|---|
| **Claude** | Anthropic Messages API | API key | Fixed list, as today |
| **Gemini** | Gemini API | API key (free tier) | Fixed list, as today |
| **OpenAI** (new) | OpenAI API | API key | List fetched from the account (`/v1/models`), filtered to chat models, with a recommended default |
| **Custom server** (new) | OpenAI-compatible Chat Completions | Optional key | Base URL entered by the user; models fetched from `/v1/models` |
| **Ollama** | Ollama API, local | None | The installed models, as today |

- **One client for OpenAI and custom servers.** The same code serves OpenAI, LM Studio, OpenRouter, Groq, Azure OpenAI and others; only the address, key and model differ.
- **Ready-made entries in the custom list:** LM Studio (`http://localhost:1234/v1`), OpenRouter, Groq. The user picks one and adds a key, or types any other address.
- **The translator stays separate**, as today: translation can use a different provider and model from the writing tools, with each provider's fastest model as the default.
- **Privacy table in Settings** gains OpenAI and custom rows. OpenAI's API terms say data isn't used for training by default; check the wording before it goes into the app. For custom servers: "depends on the server: local ones never leave your PC".
- **Safety is unchanged:** every AI translation passes the stray-letter check (`strayLetters()`), and keys never reach the card.

**Ideas**
- **A fallback chain:** if the chosen provider fails (no quota, offline), try the next one the user has set up, and say on the card which one answered.
- **Windows' own on-device AI** (the Windows AI APIs on Copilot+ PCs) as a sixth, free, offline provider. Worth checking how well it handles Arabic before promising anything.
- **Per-tool provider:** e.g. Claude for proofreading, a local model for summaries. Probably too much for Settings; a "power user" expander at most.
- **Cost hint** for paid providers: the approximate cost of the last request, from the token counts the API returns.

**Answered at the start of phase 2** (2026-10-24)
- **OpenAI's own API:** Chat Completions for everything, so one code path serves OpenAI and every compatible server. OpenAI moves to the Responses API later only if it offers something Lamha needs.
- **Plain `http://` for custom servers:** only for `localhost` and the local network. Anything on the internet must be `https://`, so a key is never sent in the clear.
- **Fallback chain:** in 3.0 only between Google and the AI, as today; between AI providers in a 3.x update.

## Folder layout

```
Lamha/
├── Lamha.slnx                    the solution
├── global.json                   pins the .NET SDK
├── Directory.Build.props         shared build settings: nullable, warnings as errors, AOT, analyzers
├── Directory.Packages.props      every NuGet version in one place
├── .editorconfig                 code style, enforced by the build
├── README.md · LICENSE · CLAUDE.md · CHANGELOG.md
│
├── src/
│   ├── Lamha.Core/
│   │   ├── Translation/          TranslationService (plan + fallback), Google, AiTranslator, StrayLetters
│   │   ├── Dictionary/           lookup, word forms, part-of-speech hint, word of the day, explain-a-word
│   │   ├── Ai/                   IAiProvider, Claude, Gemini, OpenAi (+ compatible servers), Ollama, streaming
│   │   ├── Writing/              tools and tones, prompts, proofreading results, Write new
│   │   ├── Review/               cards, scheduler, deck merge (backups), Today, streak
│   │   ├── Wikipedia/            online summaries only (offline comes later)
│   │   ├── Mistakes/             mistake journal
│   │   ├── Diagnostics/          error log, Report a problem
│   │   └── Abstractions/         interfaces for storage, clock, network, platform
│   ├── Lamha.Data/
│   │   ├── Database/             schema, migrations, repositories
│   │   ├── Dictionary/           SQLite dictionary and pack readers
│   │   └── Import/               reads Lamha 2's %APPDATA%\Lamha files
│   ├── Lamha.Windows/
│   │   ├── Input/                global hotkeys, simulated copy and paste
│   │   ├── Selection/            read the selection, keep the user's clipboard as it was
│   │   ├── Clipboard/            monitor, privacy rules (excluded programs, password managers)
│   │   ├── Automation/           UI Automation: the sentence around a word
│   │   └── Shell/                foreground window, accent colour, start with Windows, DPAPI
│   └── Lamha.App/
│       ├── App.xaml(.cs)         startup, single instance, dependency injection
│       ├── Windows/              CardWindow, MainWindow, SettingsWindow, ClipboardPanel
│       ├── Views/                pages: Translate, Today, Review, History, Clipboard, Settings sections
│       ├── ViewModels/           one per view, no UI types inside
│       ├── Controls/             Lamha's own controls: MeaningCard, GoalRing, DiffText, Flashcard…
│       ├── Themes/               design tokens: colours (light, dark, high contrast), type, spacing, corners, motion
│       ├── Motion/               the motion levels and animations (today's LamhaMotion)
│       ├── Strings/ar/ · en/     Resources.resw: every visible string
│       ├── Tray/                 tray icon and its menu
│       └── Assets/               icons, the logo mark
│
├── tests/
│   ├── Lamha.Core.Tests/
│   ├── Lamha.Data.Tests/
│   ├── Lamha.Windows.Tests/
│   └── Lamha.App.UITests/        FlaUI, on a real desktop (like today's smoke test)
│
├── tools/
│   ├── dictionary/               build_dict.py and build_packs.py, now writing SQLite
│   ├── parity/                   runs the old background and the new Core side by side
│   ├── Measure/                  the memory check
│   └── release-notes/            CHANGELOG.md → GitHub release notes
│
├── docs/
│   ├── architecture.md           this plan's architecture, kept up to date once built
│   ├── design/                   tokens, components, each screen's design
│   ├── decisions/                one short file per big decision (ADR)
│   ├── history.md · ideas.md
│   └── native-plan.md            this file
│
├── site/                         the download page
└── .github/workflows/            ci.yml, release.yml, packs.yml, site.yml
```

The dictionary data (`dict/`) moves out of the repo. It's generated, so it's built in CI and attached to releases. That keeps the repo small and its history readable.

**Ideas**
- **The first decision records** (`docs/decisions/`), written in phase 0 from this plan: WinUI 3 + C#, Windows 11 only, SQLite, no web view in 3.0, Velopack, OpenAI-compatible client. Each is half a page: the choice, why, and what was rejected.
- **A pull-request template** with a checklist: tests, screenshots in both languages and both themes, memory checked, CHANGELOG entry.

**Answered in phase 0** ([decision 0003](decisions/0003-repository-layout.md))
- **This repository:** `main` becomes Lamha 3, Lamha 2's code is in `legacy/` until phase 6, its fixes are released from `release/2.x`, and `electron-final` tags `main` before the move.
- **`dict/`'s history is kept**, with no new commits.

**As built in phase 0**, the layout above plus: `Lamha.Core/Layout` and `Text` (the first ported rules), `Lamha.App/Services` (the card, localization, the log, idle memory), `dotnet-tools.json` (Velopack's `vpk`), and `artifacts/` (git-ignored build output). `Lamha.Data` arrives in phase 2.

## UI and UX

### The design system comes first

Before any screen is built, `Lamha.App/Themes` defines:
- **Colours:** today's palette from `shared/ui.css` as XAML theme resources for light, dark and high contrast, checked by a test for WCAG AA contrast, as today.
- **Type:** Segoe UI Variable for Latin and Segoe UI's Arabic, as today (answered in phase 1).
- **Spacing:** a 4 px grid.
- **Corners:** the same three steps as today (8, 10, 14 px).
- **Motion:** today's four levels (automatic, full, subtle, off), following Windows' *Animation effects*.

### Screens to design, each with your sign-off

| Window | Contents |
|---|---|
| **The card** (the heart of the app) | Word meaning in context, other senses, synonyms, the Wikipedia summary; sentence translation; writing tools with the struck-through corrections; Write new; Replace / Insert / Copy. Borderless, rounded, Mica or Acrylic, appears next to the selection, and **doesn't steal focus** from the program the user is writing in. |
| **Main window** | NavigationView: Translate + Today, Write, Review, History, Clipboard, Settings. Mica, its own title bar. |
| **Settings** | Windows 11 Settings style (SettingsCard / SettingsExpander), the same sections as today, plus OpenAI and custom servers. |
| **Clipboard quick panel** | Alt+Shift+V: search, keyboard first, Enter to paste. |
| **First run** | Language, shortcuts, an optional AI setup, and importing Lamha 2's data. |
| **Tray menu, notifications** | The review reminder, update ready. |

**Rules that apply everywhere:**
- Arabic is right to left (`FlowDirection`), with mixed Arabic/English text checked in every view.
- Everything works from the keyboard, with numbers for the tools as today.
- Narrator announces answers when they arrive.
- High contrast works.
- Alignment and spacing are checked with screenshots of every screen in both languages and both themes (the new `npm run shots`).

Your Arabic and English product copy moves **word for word** from `shared/i18n.js`, `options/i18n-options.js` and `desktop/renderer/i18n-desktop.js` into `.resw` files, using a conversion script. Any rewording is yours to decide.

**Ideas**
- **Look up text you can't select:** a screen-region picker (like Snipping Tool) that reads the text with Windows' built-in OCR, which supports Arabic and English. That covers images, videos, PDFs that block selection, and games.
- **Read the selection without touching the clipboard:** ask UI Automation for the selected text first, and only simulate Ctrl+C when the program doesn't say. Fewer side effects, and clipboard managers don't see Lamha's copies.
- **Pin the card:** a pin button keeps the card open while the user reads or writes, and it can be dragged away from the selection.
- **Back and forward on the card** for synonym chains (today there's ← only).
- **A compact card mode:** just the meaning in context, one line, for fast readers. The full card is one key away.
- **Prices in your currency:** select a price ("$49.99", "€20", "1,200 SAR", "٥٠ ريال") and the card shows it converted to your own currency, with the rate's date. Your currency comes from Windows' region settings, and can be changed in Settings. Rates are fetched at most once a day and kept, so a conversion still works offline (marked with the date of the last rate).
- **Windows 11 widget:** the word of the day and the cards waiting for review on the Widgets board.
- **Review from a notification:** the reminder toast shows a word with *knew it / forgot* buttons, for one-card reviews without opening the app.
- **A system-wide search entry:** look up a word typed into PowerToys' Command Palette or Windows Search, with no selection needed.
- **Onboarding by doing:** the first run asks the user to select a word in a sample sentence and press Alt+Shift+L, instead of explaining it.

**Answered at the start of phase 1** (2026-10-24)
- **Arabic font:** Segoe UI's own Arabic, as today (Lamha 2's list starts with "SF Arabic", which Windows doesn't have, so on Windows it already shows Segoe UI). Nothing to download or ship. Compared on the card against Noto Sans Arabic and IBM Plex Sans Arabic, both shipped with Lamha.
- **Card background:** Acrylic with a strong tint, plus a "solid" option under Appearance. Acrylic turns solid by itself when Windows' transparency effects are off, on battery saver and in high contrast. To check on the prototype card: Windows draws a window without focus in the backdrop's inactive (solid) look, and the card never takes the focus, so the backdrop has to be told the window is active.
- **Settings:** a page in the main window, opened directly from the tray menu.
- **Ideas in 3.0:** reading the selection without the clipboard (UI Automation first, Ctrl+C when the program doesn't say), pinning the card, back and forward on the card, onboarding by doing, prices in your currency, the Windows 11 widget, reviewing from a notification, and the system-wide search entry. **In 3.x:** the compact card mode, designed after the full card is settled. OCR wasn't chosen for 3.0, so it stays right after it.

## Review and learning

Today's deck, Today and the mistake journal move over as they are. Their behaviour is defined by today's tests.

**Ideas**
- **FSRS instead of SM-2:** the newer scheduler that Anki now uses predicts forgetting better, so users get fewer reviews for the same memory. It needs a deck's history to tune, which Lamha already keeps.
- **Lamha's own sentence as the example:** the review card already shows the sentence the word was found in. Add a second example from the dictionary on the back, so the meaning doesn't stick to one sentence.
- **Export to Anki** (a `.apkg` or CSV), for users who already have a routine there.
- **Mistake journal → practice:** a short quiz built from the user's own mistakes ("a / an / the?" with their own sentences).

**Answered at the start of phase 2** (2026-10-24)
- **SM-2 or FSRS:** SM-2 in 3.0, so imported decks behave exactly as before; FSRS as a 3.x choice in Settings.

## Phases

Each phase ends with something that runs, passing checks, and a short review with you before the next starts. The sizes are relative, for planning; they aren't promises.

| # | Phase | What's done at the end | Size |
|---|---|---|---|
| 0 | **Foundations** | `release/2.x` branch and `electron-final` tag. The solution, build rules, CI (build, test, AOT publish) and Velopack test release. **The focus-safe card prototype.** A tray app with one hotkey, measured to set the real budgets. First decision records, new CLAUDE.md. | S |
| 1 | **Design system and screen designs** (runs alongside 2) | Tokens and base controls in XAML. Designs for every screen above, signed off by you. | M |
| 2 | **The brain** (Core + Data) | Settings, the SQLite dictionary and its builder, the translation service with fallback and the stray-letter guard, **the five AI providers (Claude, Gemini, OpenAI, custom, Ollama)**, writing tools, the deck (SM-2, merge), Today, history, mistakes, the Wikipedia summary, diagnostics. **Today's test files ported as the specification.** | L |
| 3 | **Windows integration** | Hotkeys, reading the selection with the clipboard put back, Replace / Insert, the sentence around a word, clipboard history and its privacy rules, tray, single instance, start with Windows, accent colour. | M |
| 4 | **The UI** | The card, main window, Settings, clipboard panel and first run, built from phase 1's designs. | L |
| 5 | **The rest** | Language packs, speech, updater, Report a problem, backup / restore. | S |
| 6 | **Moving users over** | Import from Lamha 2, a beta for you on real PCs, the last 2.x update that brings users to 3.0, release, removing the old code, README and site. | M |

### How the port stays faithful

`background.js` and the test files describe years of decisions: which sense wins, when Google gives way to the AI, how cards merge. Phase 2 ports **behaviour, test by test**. Each case in `test-writing.mjs` becomes an xUnit test with the same inputs and the same expected answer, before the C# code that makes it pass. Then the **parity tool** (`tools/parity`) runs a few hundred real words and sentences through both the old background (in Node) and the new Core with the same recorded network answers, and lists every difference for review.

**Ideas**
- **A beta channel from phase 4 on:** Velopack supports channels, so you can run 3.0 betas on your PC daily, next to the Electron app, long before release.

**Answered in phase 0**
- **Both apps side by side:** the beta uses Ctrl+Alt+L / W / V until release, then takes over Alt+Shift. Phase 0 registers Ctrl+Alt+L, next to the installed Lamha 2.

### Phase 0 results

Built on the `native/phase-0` branch:

- **Repository:** Lamha 2 moved to `legacy/` (its 255 tests still pass there); `release/2.x` branch and `electron-final` tag created locally; Lamha 2's release workflow removed from `main` (it stays on `release/2.x`); the site and packs workflows read from `legacy/`.
- **Solution:** `Lamha.slnx`, `global.json` (.NET 10.0.402, Microsoft.Testing.Platform), `Directory.Build.props` (warnings as errors, AOT-safe libraries), `Directory.Packages.props`, `.editorconfig`. Builds with 0 warnings.
- **Code:** Core (card placement, word or sentence: both ported with Lamha 2's tests), Windows (hotkeys, the tray icon, reading the selection by watching the clipboard's sequence number, pasting back, the clipboard snapshot, focus), and the App (tray, Ctrl+Alt+L, the prototype card in both languages, single instance, the Velopack hook, a log file, idle memory).
- **Tests:** 33: 28 pass and 5 clipboard tests skip in Claude's shells, which have no clipboard. They run in CI and on a desktop.
- **CI** (`.github/workflows/ci.yml`): build, test, NativeAOT publish for x64 and ARM64, the memory check, and the beta installer as a build artifact. Nothing is released.
- **Measured:** NativeAOT publish 63.7 MB on disk (without symbols), `Lamha-beta-Setup.exe` 35.7 MB, memory as in the table above, and the card opened in 32–78 ms.
- **Not yet verified:** the real shortcut → card → Replace flow in another program needs a desktop with a clipboard. It's for you to try (start the app from Explorer: Claude's shells can't reach the clipboard). The ARM64 build needs Visual Studio's C++ ARM64 tools, which only CI has.

**Ideas from phase 0**
- **Reading the selection without changing the clipboard at all**, through UI Automation's selected text, before falling back to Ctrl+C (already in the UX ideas). Phase 0's capture already leaves the clipboard untouched when nothing is selected.
- **Lamha's own pastes stay out of Win+V:** Replace's clipboard copies are marked so Windows' clipboard history and cloud clipboard skip them. Lamha 2 filled Win+V with them.

**Questions from phase 0, answered 2026-10-24**
- **The beta on GitHub:** CI artifacts only until phase 4; the first GitHub pre-release comes with phase 4. A pre-release is safe for Lamha 2 users (its updater reads only the latest release).
- **Code signing:** decided after the rest is done. Until then the installer is unsigned, and SmartScreen warns at its first install.

### Phase 1 progress

On the `native/phase-1` branch (2026-10-24), from `native/phase-3`:

- **Answers:** the four phase 1 questions and phase 3's data folder (see *UI and UX* and *Phase 3 progress*). The Arabic font was chosen from the card shown three times (Segoe UI, Noto Sans Arabic, IBM Plex Sans Arabic).
- **Tokens** (`Themes/Tokens.xaml`): Lamha 2's colours for light and dark (`shared/ui.css`, and the card's chip and skeleton from `content/styles.js`), high contrast in the user's own colours with edges in the text colour, the type scale (Lamha 2's card sizes gathered into ten, as TextBlock styles), the 4 px spacing grid, the three corners and the pill, the control height and the 24 px smallest target, and the card's Acrylic (a strong tint of the surface). The Acrylic numbers are a start, tuned with the card's design. The animation levels and timings are in Core (`Appearance/Motion.cs`: Lamha 2's motion.js and motionHint, with their numbers).
- **Tests:** 42 new, 303 in all, passing. `TokensTests` reads Tokens.xaml: every colour is Lamha 2's, text reaches 4.5:1 on every ground (tints included), field edges and the switch 3:1, every theme has every token, and high contrast uses only Windows' colours.

**Screen designs**, one at a time with your sign-off:

- **The card: signed off 2026-10-25** ([the design](https://claude.ai/artifact/LuyaNEmoXktWruuxMqR7my): eight states in both languages and themes, Acrylic or solid). Lamha 2's card (sections, colours, sizes, wording), with:
  - **Corners: Windows' own 8 px**, with its edge and shadow (what Acrylic needs; Lamha 2's 14 px would need a see-through window, without Acrylic and with more memory). The 8 / 10 / 14 steps stay for what's inside windows.
  - **Icons: Lamha 2's line icons** (drawn as paths), not Segoe Fluent Icons.
  - **Height** follows the answer, up to 540 px (Lamha 2's limit); then the middle scrolls, and the bar and the links stay.
  - **The bar:** Back and Forward as a pair once a word has been followed (Forward dimmed until you go back), so the bar doesn't jump; **Pin** (a pinned card stays open when you click elsewhere and moves by its bar); Settings; Close.
  - **Edges:** the bar, the text and the links start 16 px from the edge, on both sides and in both languages; gaps on the 4 px grid.
  - **Prices in your currency:** the price, the converted amount, and the rate with its date.
  - **New wording, approved:** Pin "تثبيت البطاقة" / "Pin the card"; pinned "مثبّتة: اضغط لإلغاء التثبيت" / "Pinned: click to unpin"; Forward "للأمام" / "Forward"; the price "بعملتك" / "In your currency", and its rate "١ دولار أمريكي = ٣٫٧٥ ريال · سعر ٢٥ أكتوبر" / "1 US dollar = 3.75 riyals · rate of 25 October".
  - Built in phase 4, from this design.
- **The main window: signed off 2026-10-25** ([the design](https://claude.ai/artifact/DKAL61WrTEaDtxdTeMm8XH): the window at its real size, 1000 × 680, clickable, in both languages and themes). One Windows 11 window: Mica, its own title bar, Windows' navigation on the start side (folding to icons when narrow), in Lamha's colours. Pages:
  - **Translate:** Lamha 2's box and its quick-lookup shortcut, Today (the goal ring, the streak, the word of the day) and the recent lookups; with text in the box, **the whole answer, as on the card** (meaning, other meanings, definitions, Wikipedia, the links).
  - **Write:** its own page, Lamha 2's Write tab (the draft, its tools, Ctrl+Enter, the corrections, your most frequent mistake), with **the mistake journal under the tools** (moved from Settings; "learn the rule" opens it there).
  - **Review:** Lamha 2's tab: due and new, the progress, the flashcard, Show meaning (Space), Forgot / Hard / Knew it (1 / 2 / 3) with their intervals, Remove from review. The count is on its navigation item.
  - **History:** a page of its own, every lookup by day, with search and Clear (Lamha 2 showed the last 12).
  - **Clipboard:** search, All / Pinned, pause, and Lamha's tools on the selected copy (translate, write it in English, summarize, add to review), pin and delete.
  - **Settings:** at the bottom of the navigation (its own design, next).
  - **Lamha 2's on/off switch** moves to the tray menu.
  - **New wording, approved:** History "السجل" / "History"; its search "ابحث في السجل…" / "Search your history…".
- **Settings: signed off 2026-10-25** ([the design](https://claude.ai/artifact/D95Szat2w3uQWEe1p1fKoR): a page of the main window, in both languages and themes). A home of **11 categories, each with its own page** (Windows 11's Settings, a breadcrumb back), in Lamha 2's order; each setting a settings card (its name and hint, the control at the end):
  - **Languages**, **The lookup card**, **Keyboard shortcuts** (each one changeable; Esc closes the card), **Writing tools**, **Translation**, **Word review**, **Without internet** (the dictionary source and the dictionaries to download), **Clipboard**, **Appearance** (theme, animations, the card's background, the accent from Windows, the wallpaper behind the window), **Privacy & history** (with your data's folder, `%LOCALAPPDATA%\Lamha Data`, Open the folder and Delete my data), and **Lamha** (the version, the updates, Start with Windows, the dictionary's sources).
  - **Writing tools: five providers**, with **OpenAI** (a key, the models it allows) and **another server** (any OpenAI-compatible one: an address, an optional key, its models, Test).
  - **No writing provider until the user chooses** (as Lamha 2); first run offers the choice.
  - **Gone with Firefox:** how the card appears, excluded sites, page translation, the buttons inside text fields; the mistake journal moved to Write; drawing with the graphics card was Electron's.
  - **New wording, approved** (as drafted on the design): OpenAI "عبر الإنترنت بمفتاح OpenAI API، مدفوع حسب الاستخدام" / "Over the internet with an OpenAI API key, paid per use", and its tip; another server "خادم آخر" / "Another server", "أي خادم متوافق مع OpenAI: LM Studio أو OpenRouter أو Groq…" / "Any OpenAI-compatible server: LM Studio, OpenRouter, Groq…", and its address, key and model lines; the card's background "خلفية البطاقة" / "The card's background" (شفافة / ثابتة, Translucent / Solid) and its hint; "بياناتي على هذا الجهاز" / "My data on this PC", "فتح المجلد" / "Open the folder", "حذف بياناتي" / "Delete my data"; Start with Windows' hint; and two edits to Lamha 2's wording: Review is a page ("من صفحة «مراجعة»"), and the privacy note names the five providers and Windows' Credential Manager.
- **The clipboard quick panel: signed off 2026-10-25** ([the design](https://claude.ai/artifact/Kg1ybqpTMG8LhzajY6hyox): six states in both languages and themes). Lamha 2's panel (380 × 460), keys and words, in the card's kind of window (Windows' 8 px corners and shadow, opening under the mouse, the focus given back to paste):
  - **The list:** search (Arabic-aware), All / Pinned, the newest copy chosen; each row its text (two lines), a pinned copy's name, an English copy's translation once it's been translated, and the number, program and time; pin and delete on the chosen row.
  - **Keys:** ↑ ↓, Enter pastes, Shift+Enter as plain text, Ctrl+1…9 that row, Ctrl+P pins, Delete deletes (with Undo), Tab the tools, Esc closes.
  - **Tools on a copy (Tab):** only those that fit it, numbered 1–9 (translate, write it in English, proofread, summarize, look up, add to review, paste as plain text); a result shows before it's pasted (Enter pastes, Esc back to the tools, Redo).
  - **Paused** (a banner with Resume) and **not turned on yet** (what it keeps and where, and Turn on).
  - **Acrylic or solid follows the card's setting** (Appearance → the card's background), one setting for both floating windows.
  - **Text only in 3.0;** copied pictures are an idea for 3.x.
- **First run: signed off 2026-10-25** ([the design](https://claude.ai/artifact/HbHwXKVAr97Y4ncKoaoCPN): the first-run window, 640 × 540, at each step, in both languages and themes). Five steps at most, one screen each:
  1. **Welcome and language** (the interface language first, so the rest is in it).
  2. **Lamha 2's data,** only when it's found, right after the language: what moves over, counted (review cards, word history, mistake journal, settings and AI keys, clipboard history); Move my data, or Start fresh; Lamha 2's folder stays. Its settings fill the later steps, which are skipped when already set. Whether Lamha 2 is removed stays phase 6's question.
  3. **Try it** (onboarding by doing): select a word in a sample sentence and press Alt+Shift+L; the real card opens. The writing and clipboard shortcuts are named under it, with Customise shortcuts.
  4. **Writing tools, optional:** the five providers; Later skips it (no AI until the user chooses).
  5. **Ready:** Start with Windows (on by default) and clipboard history (offered, off unless turned on); Start closes the window, Lamha waits by the clock.
  - **New wording, approved** (as drafted on the design): the step names (اللغة / Language, بيانات لمحة 2 / Lamha 2's data, جرّبه / Try it, جاهز / Ready), "أهلًا بك في لمحة" / "Welcome to Lamha", the Lamha 2 step's lines ("وجدنا بياناتك من لمحة 2", "تنتقل كلها إلى لمحة 3 كما هي:", "نقل بياناتي", "البدء من جديد", "يبقى مجلد لمحة 2 كما هو، فلا يضيع شيء." and their English), "جرّب أول بحث" / "Try your first lookup" and its instruction, the optional AI line, "لمحة جاهزة" / "Lamha is ready" and its line, and التالي / رجوع / لاحقًا / ابدأ (Next / Back / Later / Start).
- **The tray menu and notifications: signed off 2026-10-25** ([the design](https://claude.ai/artifact/W3usYEcZDtTPsbGtXuRmes): the menu open over the taskbar, and six notifications, in both languages and themes). Windows draws all three; Lamha gives the words and the choices.
  - **The menu** (Windows' own, so nothing but the icon stays in memory): Open Lamha (a click on the icon does it too), Clipboard, Word review (with the count), Write; **Enable Lamha** (moved from the top of Lamha 2's window), Pause clipboard history ▸ 15 minutes / 1 hour / Until I resume (or Resume, saying until when), Animations ▸, Settings, Check for updates (or Restart to install the version that's ready); the three shortcuts, greyed, one held by another program marked (unavailable); Start with Windows; Quit. The downloaded Wikipedia's reader is held back from 3.0.
  - **The icon:** a dot when words are waiting for review, faded while Lamha is turned off; the tooltip says how many to review and whether clipboard history is paused (Lamha 2's).
  - **Notifications,** titled without Lamha 2's "لمحة — " (Windows shows the app's name): **Time to review** with one word to answer (Show meaning, then Forgot / Hard / Knew it; Word review opens them all), at most one every 4 hours and only when Lamha's window isn't in use (Lamha 2's rule); **Update ready** (Restart now); **Couldn't replace**; **Shortcut in use**; and once, **after first run**, where the icon is (Windows 11 hides new icons under ^).
  - **New wording, approved:** "لمحة بجوار الساعة" / "Lamha is by the clock", and "إن لم ترَ أيقونتها فاضغط ^ في شريط المهام. حدّد أي نص في أي برنامج واضغط Alt+Shift+L." / "If you don't see its icon, click ^ on the taskbar. Select any text in any program and press Alt+Shift+L."

**All six screens are designed and signed off** (2026-10-25); the design pages are in [docs/design/](design/). Phase 4 builds them from these designs, with the new wording above going into the .resw files next to Lamha 2's.

**Base controls** (`Themes/Controls.xaml`): Lamha's buttons (filled, quiet, delete, icon), chips (a ToggleButton), the switch in Lamha 2's green, and the boxes the designs repeat (a panel, the meaning's tinted box), all from the tokens, with edges in the text colour in high contrast (a new token, LamhaControlEdgeBrush). App.xaml merges Controls.xaml, which merges the tokens. Tests check that every Lamha resource the XAML names exists and that the switch is the token's green; the app starts with them. Not yet seen on a screen: phase 4's windows are the first to use them. Settings' cards come with CommunityToolkit's SettingsCard in phase 4, once it's checked to publish with NativeAOT.

**Phase 1 is done** (2026-10-25): the questions answered, the tokens and base controls built, and all six screens signed off. Phase 4 (the UI) can start from it.

### Phase 2 progress

Built on the `native/phase-2` branch (2026-10-24):

- **Lamha.Data:** the user's database (`lamha.db`: settings, history, the deck with its removals, the day's practice, the mistake journal, the error log) with numbered schema steps, one connection, WAL, and a 512 KB page cache. The **dictionary file**: Lamha 2's 22 MB of JSON shards become one read-only SQLite file (24 MB, built in 1.2 s by `tools/DictionaryBuilder`), read word by word.
- **Lamha.Core:** Lamha 2's background.js, local-dict.js and lamha-ai.js ported rule by rule, with their numbers: the lookup (dictionary, Google, the AI, language packs, the word in its sentence, explanations in other languages, the history, automatic cards, milestones and the goal), Google with its two addresses and cooldowns, the translation service with the Google ↔ AI fallback and the stray-letter guard, the **five AI providers** (decision 0010), the writing tools and the mistake journal, the deck (SM-2, the queue, the streak by the calendar), Today and the word of the day, backups in Lamha 2's format (its copies restore here), the Wikipedia summary, and Report a problem. Errors are Lamha 2's codes.
- **Tests:** 161 in Core and Data (all pass, five runs in a row), most of them Lamha 2's test-writing.mjs cases with the same inputs and answers, on the real dictionary and the real SQLite stores, with a fake network and a fake clock (Lamha 2's timeouts pass without waiting). Not ported, because what they test is gone or comes later: the Firefox link and page translation, the downloaded Wikipedias (on hold), installing packs (phase 5), the Arabic duration wording (with the strings, phase 4), the key-saved flags (Firefox's content scripts), and keeping Arabic for updated users (the importer, phase 6).
- **Changed from Lamha 2, on purpose:** the Claude models are the current ones (Opus 5.5 by default, Haiku 5.5 for translation); the writing tools' system prompt says "an app … in any program" instead of "a browser extension … on a web page"; Lamha 2's toDateString days are calendar dates (backups still use its format); results carry part-of-speech codes, which the app names in its own strings.

**Left in phase 2**
- **The parity tool** (`tools/parity`): a few hundred real words and sentences through Lamha 2's background and the new Core with the same recorded answers, every difference listed.
- **Recorded answers from LM Studio, OpenRouter and Groq** (decision 0007); the tests use answers written by hand.

**Moved to the phases that use them:** wiring Core and Data into the app, with keys in Credential Locker and Windows' network status (done in phase 3); the dictionary file in CI and the installer (done in phase 3); answers streamed word by word (with the card, phase 4); the new error codes' wording (phase 4); language pack files (phase 5); importing Lamha 2's files (phase 6).

**Questions from phase 2**
- **Wording for the new errors** (Arabic and English): `openai_no_key` (OpenAI chosen without a key), `custom_no_server` (a custom server without an address or a model), `custom_insecure` (plain `http://` to an internet address), `custom_offline` (the custom server can't be reached), `google_error` (Google answered with something unexpected). Recommendation: Claude drafts them in Lamha 2's style for you to reword. *(phase 4)*
- **The writing tools' system prompt** now says Lamha is "an app" where the user selects text "in any program". It's never shown, but it's your wording: keep it? *(phase 4)*

### Phase 3 progress

Built on the `native/phase-3` branch (2026-10-24), on top of phase 2:

- **The brain in the app** (`Services/Brain.cs`): `lamha.db`, the settings and clipboard history open at start; everything a lookup needs (HTTP, the dictionary file, the AI) is built at the first lookup. API keys are in Windows' Credential Manager ("Lamha/claude"…, this PC only), the network state comes from Windows, and the dictionary file ships next to Lamha.exe (`dict\lamha-dict.db`, built by CI before publishing). `--profile=folder` runs Lamha on other data (the memory check uses it).
- **The shortcuts:** Ctrl+Alt+L looks up the selection on the prototype card, now with the real lookup (dictionary, Google, the AI); Ctrl+Alt+W opens it to edit the text. Replace pastes over the selection; with nothing selected, Insert pastes at the cursor. When pasting fails, the text stays on the clipboard and a notification says so (Lamha 2's words). A shortcut another program holds is announced the same way, and marked in the tray menu.
- **The sentence around a word:** read through UI Automation before the card takes the focus, on its own thread, at most 700 ms ([decision 0012](decisions/0012-sentence-through-ui-automation-in-process.md)).
- **Clipboard history:** the listener, Lamha 2's privacy rules in its order (programs' own "don't keep this" marks, excluded programs never read, bank-card numbers), merged duplicates, the size and age limits, Arabic-aware search, pinning, labels, undo; each copy encrypted for the Windows user ([decision 0011](decisions/0011-clipboard-history-encrypted-in-sqlite.md)). Lamha's own Ctrl+C isn't recorded.
- **The tray:** the shortcuts, clipboard history on/off and its pause (15 minutes, 1 hour, until resumed), Windows' accent colour, Start with Windows (which also respects being switched off in Windows' Startup settings), Quit. The tray icon now comes back after Explorer restarts (phase 0's message-only window never got that broadcast).
- **Windows' accent colour:** Lamha 2's palette rules (readable in both themes, its exact colours), followed live when the colour changes in Windows.
- **Tests:** 261 (Core 221, Data 12, Windows 28), all passing; Lamha 2's test-clipboard.mjs store, search and privacy cases and test-desktop.mjs's accent case are ported. Found while porting: phase 2's Arabic normalizer dropped Arabic-Indic digits (its mark range was too wide); fixed, with a test.
- **Measured** (NativeAOT, x64, `measure.ps1 -Budget`): tray 0 / 48 MB, card open after a real lookup 36 / 77 MB, card closed 1 / 77 MB (Task Manager / commit), all within budget. On disk 95.8 MB, of which the dictionary file is 24 MB.

**Not yet verified:** the shortcuts in real programs, the sentence from Word, Notepad and a browser, Replace and Insert, and clipboard history recording your copies (the tray's switch turns it on). These need a desktop: start the beta from Explorer.

**Moved to phase 4, with the windows that use them:** the clipboard panel and its shortcut (Ctrl+Alt+V in the beta), Lamha's tools on a copy (translate, write in English, summarize, add to review), the review reminder and the tray's Open / Review / Write / Settings items. The tray's clipboard and accent switches (Proto_ strings) move into Settings then.

**Questions from phase 3, answered 2026-10-24**
- **Where the user's data lives:** in its own folder, `%LOCALAPPDATA%\Lamha Data` (decision 0006). `%LOCALAPPDATA%\Lamha` is Velopack's install folder, and uninstalling Lamha deletes it; the data folder is never touched by an uninstall or a reinstall, and "delete my data" becomes a choice in Settings. `lamha.db` and the log move there. **To build before the first beta release** (phase 4's pre-release), along with CLAUDE.md's log path.
- **Reading the selection without the clipboard:** yes, in 3.0. The sentence reader already finds the selection through UI Automation, so it reads the selection first and falls back to Ctrl+C.

### Phase 4 progress

On the `native/phase-4` branch (2026-10-25), from `native/phase-1`.

**Answered at the start of phase 4** (2026-10-25)
- **Order:** the foundations, then **the card**, then the **first GitHub pre-release** (the beta channel, so it's used daily), then the main window, Settings, the clipboard panel, first run, and the tray and notifications, with a new beta after each.
  - **Changed 2026-10-25:** no new beta until Lamha can update itself (phase 5's updates); until then each slice is merged into `main` without a release.
- **Wording for phase 2's new errors** (title · the line under it), approved as drafted in Lamha 2's style:
  - `openai_no_key`: أضف مفتاح OpenAI · أنشئ مفتاحًا من platform.openai.com وأضفه في الإعدادات. / Add an OpenAI key · Create a key at platform.openai.com and add it in Settings.
  - `custom_no_server`: أكمل إعداد الخادم · أضف عنوان الخادم واختر نموذجًا من الإعدادات. / Finish setting up the server · Add the server's address and choose a model in Settings.
  - `custom_insecure`: لا يمكن استخدام عنوان الخادم · استخدم عنوانًا يبدأ بـ https:// (يعمل http:// على هذا الجهاز وشبكتك المحلية فقط). / The server's address can't be used · Use an address that starts with https:// (http:// works only on this PC and your local network).
  - `custom_offline`: الخادم لا يستجيب · تأكد أن الخادم يعمل وأن عنوانه صحيح في الإعدادات، ثم أعد المحاولة. / The server isn't answering · Make sure the server is running and its address in Settings is right, then try again.
  - `google_error`: Lamha 2's general error, حدث خطأ · أعد المحاولة. / Something went wrong · Try again.
- **Lamha 2's errors that name Claude** (`ai_bad_key`, `ai_no_credit`, `ai_forbidden`) name the provider in use, and its site for credit (Claude: console.anthropic.com, OpenAI: platform.openai.com, another server: "the service's website"); `ai_no_key` lists the five providers.
- **The writing tools' system prompt** ("an app… in any program"): kept.
- **The first beta without Settings** (answered 2026-10-25): it ships with lookups only (the dictionary, Google, Wikipedia); the writing tools say they aren't set up yet. AI keys come with Settings, in the next beta.

**Foundations**
- **Lamha 2's strings:** `tools/Strings/lamha2-strings.mjs` reads Lamha 2's four string files into `tools/Strings/lamha2-strings.json` (722 strings; 92 more are functions, mostly Arabic counting, ported by hand), finds a string, and writes the `.resw` lines for a key. `ResourcesTests` checks that both languages have the same strings and placeholders, and that every string whose comment names a Lamha 2 key keeps its wording (an approved rewording says "reworded" in its comment).
- **The interface language** comes from the setting (Settings → Languages; "automatic" follows Windows, as in Lamha 2), read at start.
- **The data folder** is `%LOCALAPPDATA%\Lamha Data` (decision 0006), with the log in its `logs` folder for every profile. A `lamha.db` left in the install folder by phase 3's builds moves there once.

**The card** (built from the signed-off design, replacing the prototype; `ViewModels/Card`, `Views/CardView`, `Windows/CardWindow`, `Controls/`):
- **The answer,** Lamha 2's `render(d)` part by part: the headword, its sound and what it's a form of; the review bookmark; "did you mean"; the hero (the meaning in the sentence with the sentence's translation around it, the AI's guess, the explained view's definition, roots and plural), right to left in both languages as in Lamha 2; other meanings by part of speech (Google's chips with "+n", the offline dictionary's senses with their hints, "Other meanings (n)"); definitions numbered, the one that fits the sentence tinted, their Arabic filled in after the card shows; examples; the Wikipedia summary; milestones; Google Translate, Cambridge, Better translation and Writing tools at the foot. Narrator reads the answer when it arrives.
- **The bar:** Lamha's mark, the languages or العربية ⇄ English for a word, where the answer came from (book or sparkle), Back and Forward as a pair once a word has been followed (Alt+← / Alt+→ too), Pin (a pinned card stays, and moves by its bar), Close. Settings shows once the main window exists.
- **The writing tools:** the tools for the text (1–9), the proofread's struck-through corrections with their kind and why, the other tools' text, Replace / Insert / Copy / Try again, the summary in the other language, Reply's and Write new's request (kind, tone, Ctrl+Enter).
- **Errors** in Lamha 2's words with the provider named (`Services/ErrorText.cs`); "Select some text first" as Lamha 2's hint by the mouse, without taking the focus.
- **The window:** Acrylic with the tokens' tint, always drawn active (`CardBackdrop`), or solid (a new setting, `cardSolid`, for Appearance); its height follows the card up to 540 px, then the body scrolls (`CardPlacement.Resize`: a card under the mouse grows down, one above it grows up).
- **Seen** on screenshots in both languages (a word from the offline dictionary, a phrase, the writing tools not set up yet); **measured** (NativeAOT, x64): tray 0 / 49 MB, card open 35 / 77 MB, card closed 1 / 77 MB, within budget. `--sample=text` (or `write:text`) and `--lang=ar|en` set the sample card for screenshots.

- **Prices in your currency** (Core's `Money/`): a selected price alone ("$49.99", "1,200 SAR", "٥٠ ريال", "20 dollars") shows in the user's currency (the new `currency` setting, else Windows' country or region) with the rate and its day. Rates from ExchangeRate-API's open service (chosen 2026-10-25: free, no key, ~160 currencies), fetched at most once a day and kept, so prices convert offline; its credit, "Rates By Exchange Rate API", is the link at the foot (its terms ask for it). Currency names for 32 currencies in both languages (new wording, to review). 36 tests.
- **The shimmer** (Lamha 2's `.sk`), still when animations are off; **the theme setting** (light, dark, auto) on the card; `--theme=light|dark` for screenshots.
- **Found by running the published build:** C#/WinRT needs `AllowUnsafeBlocks` to pass a .NET list to WinUI under NativeAOT; without it the card failed only when published. Its AOT warnings are now errors, so the build catches it.
- **Seen** in both languages and themes: a word, a phrase, a sentence, a price, the writing tools not set up, the hint. **Measured** (NativeAOT): tray 0 / 49 MB, card open 39 / 81 MB, card closed 1 / 81 MB, within budget. 344 tests.

**The beta release** (`.github/workflows/beta.yml`): started by hand (Actions → Beta → Run workflow, on the branch to release), it builds, tests, publishes and packs as CI does, then publishes **v3.0.0-beta.N** as a GitHub pre-release with the x64 and ARM64 installers (Velopack's `beta-win-x64` / `beta-win-arm64` channels) and the notes typed when starting it. Lamha shows the same number. GitHub offers "Run workflow" only once the file is on `main`.

**The last checks before the beta** (2026-10-25): the error state (`--sample=error:code` shows a code's error through the card's own path; Try again then looks up for real) and a followed, pinned card, clicked through UI Automation, seen in both languages and themes, Debug and published. Found and fixed:
- **The published card had a title bar,** could be resized and wasn't on top: under NativeAOT `AppWindow.Presenter is OverlappedPresenter` is false (C#/WinRT returns the base type), so none of its settings applied. `As<OverlappedPresenter>()` works. Phase 4's earlier published screenshots were cropped below it.
- **Icons in a card whose theme isn't Windows'** (a light card on a dark Windows) were white, and a disabled Forward didn't dim: an icon took the app's theme's default colour, not its button's. It now follows its button.
- **Narrator:** the meaning chips were named by their gloss, not the word on them; the foot links and the error's Try again had no name.
- **Measured** (NativeAOT): tray 0 / 49 MB, card open 40 / 84 MB, card closed 1 / 84 MB, within budget but 1 MB under the card's 85 MB commit budget. Applying the card's window settings costs about 3 MB of it.

**The first beta, v3.0.0-beta.1,** was released from `native/phase-4` before the checks above, so it has the published card's title bar; beta.2, from `main`, has the fixes. Listen waits for speech (phase 5).

**The main window** (built from the signed-off design, a slice at a time on `lamha3/main-window`; `Windows/MainWindow`, `ViewModels/Main`, `Services/MainWindowService`):
- **The frame** (2026-10-25): Mica, its own title bar (Lamha's mark and name; Windows' buttons at its end, on the left in Arabic, where the window is laid out right to left as Windows' Arabic programs are), the pages on the start side (Translate, Write, Review, History, Clipboard; Settings at the foot) and the page's title on Windows' content layer. The side pane is a list in Windows' navigation style rather than `NavigationView`, so its icons are Lamha's line icons as designed; narrower than 720 px it folds to icons, with the names as tooltips. Opened by clicking the tray icon or starting Lamha again (until now both opened the sample card), in front of other windows; closing it ends it. `--page=translate` (or write, review, history, clipboard, settings) opens it at start for screenshots.
- **Seen** in both languages and themes, Debug and published, wide and folded; in Arabic the title bar drags, its buttons answer on the left, and clicks and UI Automation land where things are drawn. **Measured:** open 18 / 86 MB, back in the tray after it 4 / 86 MB, within the provisional budget (decision 0013, which also gives .NET's spare memory back when Lamha goes idle: the card closed is 82 MB now).
- **Translate + Today** (2026-10-25; `Views/TranslatePage`, `ViewModels/Main/TranslateViewModel`, `TodayItem`): Lamha 2's box (an answer 420 ms after typing stops, at once on Enter, Shift+Enter for a new line, × to empty it) with the design's field (its edge, and the 2 px accent ring while it has the focus) and the quick-lookup shortcut as keys. Under it **the whole answer as the card shows it:** the card's answer is now one control, `Views/AnswerView`, used by both (the meaning's Copy and the bookmark moved onto `WordAnswer`, with the filling-in of glosses and Wikipedia); the page leaves out the text already in its box. With the box empty, **Today** (the goal's ring, `Controls/GoalRing`, green when met, as in Lamha 2; the streak from 2 days, with Arabic counting; the word of the day and "See its meaning") and the **12 recent lookups** in two columns (one when narrow), each looked up again on a click; Clear empties the history with "History cleared · Undo" for 5 s (Lamha 2's toast, now the window's, `MainViewModel.ShowToast`). Errors in the card's words. The window opens with the caret in the box; its pages now fill the content layer up to the design's 760 px (they took their content's width before).
- **Answered 2026-10-25:** the recent lookups step aside while an answer shows, as designed (Lamha 2 kept them); **a price typed in the box shows in your currency**, as on the card. The card's price is now one control too, `Views/PriceView`, and `PriceAnswer` finds the price, copies it and carries the rates' credit for both. Seen in both languages and themes, Debug and published.
- **Found while trying it:** settings saved without a key (a setting added after they were saved, such as `currency`) read back as null or 0, not the default, and every lookup then failed with an exception nothing caught: the card and the page waited for ever. Settings are now read as Lamha 2 did (the defaults, then what was saved, `SettingsService`), and an unexpected fault in a lookup shows Lamha 2's "Something went wrong · Try again" and is logged by its type.
- **Seen** in both languages and themes, Debug and published, wide and folded (a word, a sentence, the "Offline only" error, Today with the goal met and a streak, Clear and Undo through UI Automation). **Measured** (NativeAOT): main window open 26 / 92 MB, back in the tray after it 4 / 90 MB, within the budgets; the card unchanged (40 / 84, 2 / 83). 347 tests.
- **Left for their pages:** "Writing tools" under a text's answer opens the Write page once it exists (done with Write); the Settings button in an error ("Offline only") waits for Settings; Listen waits for speech (phase 5).
- **History** (2026-10-25; `Views/HistoryPage`, `ViewModels/Main/HistoryViewModel`): the whole history by day on this PC's calendar, named Today, Yesterday, the weekday for the rest of the week, then the date (with the year when it isn't this year's); each row the word, its meaning, the time and the review bookmark (English words with a meaning, as on the card; in the accent colour while in review), and a click looks the word up again on Translate. **Search** as clipboard history's (every word of the query in the word or its meaning, Arabic spelling variants, marks, digits and case aside; Esc empties it); **Clear** with "History cleared · Undo", as on Translate. "No words yet" when the history is empty, "No results" when nothing matches. Core: `HistoryService.ByDay(query)` and `ReviewService.InReviewAsync(words)` (the bookmarks in one go), 4 tests. Rows are 44 px with or without the bookmark, and the times have a column of their own so the meanings line up; the word starts where the interface starts (under the day's name) in both languages.
- **Seen** in both languages and themes, Debug and published (search in Arabic and English, no results, the bookmark, Clear and Undo, a row opening Translate, all through UI Automation). **Measured** (NativeAOT): main window open 26 / 92 MB, back in the tray 4 / 90 MB; the card 41 / 85 (at its commit budget, as before). 350 tests.
- **Answered 2026-10-25:** **the history keeps 1,000 words** (Lamha 2 kept 100), so the page has more than a few days in it; a word stays "known" for milestones and the goal while it's in there, a backup carries all of them (Lamha 2's restore keeps the newest 100), and the review list's one-time import of the history still takes Lamha 2's 100. **New wording, approved:** the empty history's line under "No words yet" (Lamha 2's `rv.emptyTitle`): "تظهر هنا كل كلمة تبحث عنها، مرتّبة حسب اليوم." / "Every word you look up appears here, by day." A word bookmarked from History gets only its meaning (the history keeps no sentence or definition).
- **Write** (2026-10-25; `Views/WritePage`, `ViewModels/Main/WriteViewModel`, `JournalViewModel`): Lamha 2's Write tab. The draft (kept between visits, as Lamha 2's `draft`), the design's field with its accent ring; under it the tools for the draft (English: Proofread, Improve, Formal, Friendly, Shorter, Longer; Arabic: In English, Lamha 2's short names), greyed until there's a draft and the writing tools are set up, with Ctrl+Enter after the last (Proofread, or In English for Arabic). The result as on the card: Proofread's struck-through corrections with each kind and why, the other tools' text; **Replace** puts it into the draft to keep working on it (Lamha 2's `write.useIt`), Copy, Try again (asked afresh). Not set up yet: Lamha 2's first step ("set up the writing tools") as soon as the page opens. **Your most frequent mistake** under the panel ("Verb tenses (7) — learn the rule →") opens the journal at that kind. **The mistake journal** (moved from Lamha 2's Settings, its `renderJournal()`): the summary with Arabic counting, a bar per kind (most frequent first, all starting where the longest name ends), a kind's rule and examples on a click, the 30 latest mistakes, Record my mistakes, and Clear the journal. "Writing tools" under a text's answer on Translate now opens Write with the text in the draft (offered once the writing tools are set up, as on the card). Core: `WritingService.Journal()`, `MostFrequent()`, `ClearJournal()` and `RestoreJournal()`, 1 test. All strings are Lamha 2's but one.
- **Answered 2026-10-25:** **Clear the journal works as History's Clear:** no question (Lamha 2 asked one), and "Mistake journal cleared · Undo" for 5 s. Its line is new wording, a draft to reword: "مُسح سجل أخطائي" / "Mistake journal cleared" (as Lamha 2's "مُسح السجل"). **The corrections keep the card's heading** ("التصحيحات (٢)"), which the design left out, so the card and the page match.
- **Found while building it:** a kind's name with English in brackets ("حروف الجر (in / on / at …)") drew as "(… in / on / at) حروف الجر" on the page and on the card's corrections (Lamha 2 isolated the bracket in a bdi); left-to-right marks around it now (`Localization.Category`); WinUI's text ignores Unicode's isolates. The card's Replace and Copy (an icon and a word) had no name for Narrator; named, as on Write.
- **Seen** in both languages and themes, Debug and published, with a local stand-in for an OpenAI-compatible server (Proofread, Improve, In English, Replace, learn the rule, Clear and Undo, Translate's "Writing tools", not set up yet, the empty journal), all through UI Automation. **Measured** (NativeAOT): the budgets unchanged (main window on Translate 26 / 92 MB, after it 4 / 90); on Write after a proofread 74 / 96 MB, within 90 / 110. 352 tests.
- **Review** (2026-10-25; `Views/ReviewPage`, `ViewModels/Main/ReviewViewModel`): Lamha 2's Review tab (popup.js `loadReview`, `renderReview`, `answer`). Due and new as pills (quiet at zero, Lamha 2's `.zero`), the words and how many are learned; this sitting's progress from the start side once a card is answered; the flashcard: a new word's tag, the word, its sentence with the selected form marked, Show meaning (Space or Enter), then the meaning (or, for an English–English word, its definition first), Forgot / Hard / Knew it (1 / 2 / 3) with when each brings the word back, and Remove from review. With nothing left: what this sitting did, the next review, and the review streak from two days; with no words, Lamha 2's "No words yet". Today's goal reached by an answer says so. **The count on Review** in the pane (due + new, Lamha 2's `rvBadge`), on opening and after each answer. Arabic counting ported as separate strings, as Write's (`Review_Words*`, `Span_*` with their forms after بعد and after a verb). Listen waits for speech (phase 5).
- **Review's motion** (Lamha 2's `flyOff` and `renderReview`, with their numbers): the answered card's picture flies off the way it was answered (Knew it toward the end of the line, so left in Arabic; Forgot toward its start; Hard drops), 280 ms, full motion only, the buttons left behind; the next card rises in (240 ms, 110 ms later at full); the meaning fades in (180 ms); "Well done" pops in after the last card (300 ms). A quarter faster at subtle, none at off. Lamha 2's confetti burst on "Well done" isn't ported yet.
- **Found while trying it:** a window opened on Review had the pane's focus, so Space chose Translate; the card now takes the focus (as if clicked, like the pane's). **Seen** in both languages and themes with five cards put into a test profile (front, back, mid-sitting, done, empty, each answer's flight caught mid-way). 352 tests.
- **Measured, and over budget before this change too** (NativeAOT, this PC, 2026-10-25 evening): tray 1 / 55–56 MB against 35 / 55, card open 27 / 86–89 against 70 / 85; c2d6642 (before Review), published and measured the same way, gives the same. The main window is within its budgets (25–26 / 91–95). For the user to decide: look for the extra commit, or raise the budgets with a decision record.
- **A flaky test** (not new): under the whole suite's load, `LookupTests.AWordLookedUpAgainGoesBackToTheTopOfTheHistory` sometimes counts 4 network calls for 3 (and `WikipediaTests…GivesUpAfter4SecondsThenRests` once failed). `TestLamha.Timed` moves the fake clock 100 ms for each real millisecond a lookup is still running, so on a busy PC "online first" runs out of its wait, the dictionary answers, and that answer isn't cached. Both pass alone (20 runs).
- **Clipboard** (2026-10-25; `Views/ClipboardPage`, `ViewModels/Main/ClipboardViewModel`): Lamha 2's clipboard tab, as the design simplified it (the chosen copy opens in place, not on a page of its own). Search (Arabic-aware, Esc empties it), All / Pinned, Pause (15 minutes, 1 hour, until I resume: the tray's), and the copies newest first with pinned ones on top, 100 at a time with Show more: each its name, its text (two lines, in its own direction), an English copy's translation once it's been translated, the program and when ("قبل ٥ دقائق", Lamha 2's `relTime` with its Arabic counting). The list starts on the newest copy below the pinned ones; ↑ ↓ and Page Up / Down choose, Ctrl+P pins, Delete deletes with Undo. **The chosen copy:** the tools that fit it (Lamha 2's `available()`: Translate, Write it in English, Proofread, Summarize, Look up, Add to review), pin and delete; a tool's answer under them (the text, Proofread's corrections, the summary with its language switch, remembered), Copy and Redo, and "Saved result" when it came from the copy. Look up opens Translate. **Off** (the default): the panel design's "Turn on clipboard history"; **paused**: its banner and Resume. New copies appear while the page is open; leaving the page, or closing the window, lets go of the decrypted copies (`ClipboardHistory.Unload`).
- **Core:** `ClipActions` (Lamha 2's desktop/clipboard-actions.js): each tool through the usual services, so the dictionary mode, the AI provider and the mistake journal apply; results kept with the copy (now Proofread's too, `ClipCache.Proofread`), Redo asks again and isn't counted in the journal twice; Add to review reads the list before the lookup, and gives the card the dictionary's example. Lamha 2's test-clipboard.mjs action cases ported, 12 tests. `ClipboardHistory.UpdateCache` (a copy deleted meanwhile is left alone).
- **The count on Review** is now read again whenever another page opens, and at once after Add to review here.
- **New wording, from the clipboard panel's signed-off design:** the paused banner's line "لا يُسجَّل ما تنسخه الآن." / "What you copy isn't being recorded." and "استئناف" / "Resume". Not on this page (Lamha 2 had them): a copy's own page (its facts, its name, "ignore this program") and "Clear unpinned"; they belong to Settings → Clipboard, or a later look at this page.
- **Seen** in both languages and themes with copies put into a test profile through Lamha's own encryption (the list, a translation, Proofread without a writing provider, Add to review, paused, off), tools pressed through UI Automation. **Measured** (NativeAOT): the main window 25 / 92–93 MB, within its budgets; the tray and the card as before (over by 1–3 MB of commit, see above). 364 tests.
- **Settings, first slice** (2026-10-25, on `lamha3/settings`; `Views/SettingsPage`, `ViewModels/Main/SettingsViewModel`, `Controls/SettingsCard`): the home of 11 categories in Lamha 2's order (two columns, one when narrow; each its icon, its name and what's in it), each opening its own page with the breadcrumb back ("Settings › Languages"; Alt+← too). The focus goes to the breadcrumb when a category opens and back to its tile on returning. Built so far: **Languages** (the interface language, with Lamha 2's "The change applies when Lamha starts again" while it differs from the one in use; the translation language; Arabic into English), **The lookup card** (the sentence, the languages explained in their own language as chips, the translation language's always on; definitions, Wikipedia, speak automatically), **Translation** (automatic / Google / AI, and who translates; the model per provider comes with Writing tools), **Word review** (its reworded intro, add automatically, new words per day, the goal, Delete all cards after Lamha 2's question), **Without internet** (the three dictionary sources; the dictionaries to download are phase 5's) and **Appearance** (theme, animations, the card's background, the accent from Windows, the wallpaper behind the window). Each change is saved at once; the theme and the wallpaper apply to the open window at once, the rest at the next card. Changes made elsewhere (the card's language switch) show on the page while it's open.
  - **A settings card of Lamha's own** (a border and two lines from the tokens) rather than CommunityToolkit's SettingsCard: one package fewer to check under NativeAOT. The switch ends where the dropdowns end (Windows keeps 12 px for "On" / "Off"), and the radio choices are the design's ring in the button colour.
  - **New setting:** `windowMica` (Lamha 2's name), **on by default**, unlike Lamha 2's: the main window is designed on Mica; off shows Lamha's page colour. The tray's prototype accent switch is gone (now in Appearance).
  - **Found while building it:** an Arabic hint that starts with a Latin word ("Google، والذكاء الاصطناعي…") was laid out left to right; Settings' texts now follow the window's direction (`TextReadingOrder`).
  - **Seen** in both languages and themes, Debug and published, every category opened and closed through UI Automation, the wallpaper switched off and on. **Measured** (NativeAOT, alternating with `main`'s build): the same as `main` (tray 1 / 55–56, card 26–27 / 86–87, main window 25–26 / 92–93 MB), so the tray's and card's 1–2 MB over their commit budgets is still the earlier question. 364 tests.
- **Settings, Writing tools** (2026-10-25, same branch; `ViewModels/Main/WritingSetupViewModel`, `Controls/Markup`, `Controls/StatusText`): the five providers as designed, the chosen one's setup under it. Lamha 2's `initAI()` / `initKeyBox()` / `refreshOllama()` / `testOllama()`: **Gemini, Claude and OpenAI** take a key that's checked with a tiny request before it's saved in Credential Manager (OpenAI's check is its list of models, which then fills the dropdown), shown afterwards only as "•••••••• (saved)", with Remove; **Ollama** its address (saved when the field is left), Refresh list (a Qwen model chosen the first time, as Lamha 2), the model and Test, timed; **another server** its address, optional key, the models it lists and Test. Failures in Lamha 2's Settings words (`oerr.*`), else the card's. Lamha 2's strings keep their little HTML (`<b>`, `<a>`, `<code>`), drawn by `Markup`, links in Lamha's accent. The intro names the shortcut in use (Ctrl+Alt+W in the beta).
  - **Translation** gains the model it translates with (Claude's and Gemini's, the quickest first; for Ollama, OpenAI and another server "Same as the writing tools (model)" and the models listed) and Lamha 2's line: "Sentences are translated with Claude", or that it isn't set up yet.
  - **Languages** gains **your currency** (answered 2026-10-25: under Languages): automatic (Windows' region) or one of the 32 currencies Lamha has names for (Core's `Prices.Named`).
  - **Every error's Settings button now works:** the card's (and its bar's Settings), Translate's, Write's and Clipboard's open Settings at the category that fixes it (Writing tools, Translation, Without internet).
  - **Drafts to approve:** "Your currency" / "عملتك", its hint and "Automatic ({0}, from Windows' region)"; `o.trNotReady` without "below"; "✓ Works (n s) — the writing tools are ready." for another server's Test (Ollama's says "free, on your PC"). The Claude model names are Lamha 3's (Opus / Sonnet / Haiku 5.5).
  - **Found while trying it:** opening Writing tools with Ollama chosen started a Test by itself (the dropdown sets its choice back when it's made); Test now runs only for a model the user picks.
  - **Seen** in both languages and themes, Debug and published, with a local stand-in for Ollama and an OpenAI-compatible server (Refresh list, Test, a model chosen, Ollama not running), a wrong Claude key (refused, not saved), and Write's "Turn on the writing tools" → Settings → Writing tools, all through UI Automation. 364 tests.
- **Settings, Clipboard** (2026-10-25; `ViewModels/Main/ClipboardSetupViewModel`): Lamha 2's desktop/renderer/clipboard/settings.js. Turn on (off asks Lamha 2's question: Delete, Keep it, or Cancel/Esc, which leaves it on), items to keep, delete unpinned items after, skip bank card numbers, the excluded programs (Remove each; a name typed is checked as Lamha 2's `exeName()`, and when the history has copies from it Lamha 2 asks whether to delete them), "From your history:" (the programs seen, one click excludes them), Clear unpinned / Clear all after their questions, and "🔒 The history is encrypted on this PC". The tray's prototype switch is gone; the tray pauses. Lamha 2's intro line names the clipboard panel's shortcut, which comes with the panel: left out until then.
  - **Questions** (`IPageHost.AskAsync`) now take Lamha 2's line under the question and a second choice; the destructive choice is the tokens' solid red, and nothing is in Windows' blue (Esc still cancels).
  - **The switch's knob is white when on,** in both themes, as designed (dark had Windows' black knob).
  - **Seen** in both languages and themes through UI Automation: on, a bad name, a name added and removed, off with Keep it, Clear all's question and Cancel. 364 tests.
- **Settings, Privacy & history** (2026-10-25; `ViewModels/Main/PrivacySetupViewModel`): keep a history of words; Clear history with how many words are saved (as the History page's Clear: no question, Undo for 5 s); **a copy of my data** (Save a copy as `lamha-YYYY-MM-DD.json` and Restore, through Windows' file dialogs from the Windows App SDK's Foundation, Core's `BackupService` in Lamha 2's format; Brain now has it); **Report a problem** (Copy, Report on GitHub with the report in a new issue cut to fit an address, and "Show the report" to read it first: Lamha 2's `initReport()`); **my data on this PC** with Open the folder.
  - **Not built yet, questions for you:** *Delete my data* (what happens after: Lamha quits and starts fresh? the keys in Credential Manager too?) and *What is sent, and where?* (Lamha 2's text names page translation and only Claude and Gemini: it needs your new wording for the app and five providers).
  - **Seen** in both languages and themes; the Save / Open dialogs themselves are Windows' and weren't driven through UI Automation here: to try on the desktop.
- **Settings, Lamha** (2026-10-25): the version (kept left to right in Arabic), Start with Windows (the tray's `StartWithWindows`, read each time the page opens), All releases on GitHub, and the offline dictionary's sources (Lamha 2's list; the downloaded dictionaries' line comes back with them in phase 5). **Automatic updates and Check now wait for the updates themselves** (phase 5): not shown, and the tile's summary doesn't name them until then. Start with Windows wasn't switched in testing (it writes the real Run key).
- **Settings, Keyboard shortcuts** (2026-10-25; `Services/ShortcutService`, `ViewModels/Main/ShortcutsSetupViewModel`): the quick lookup and the writing tools, each with its keys drawn as key caps and Change (then press the new keys; Esc cancels), and Esc closing the card. New in Lamha 3 (Lamha 2's were fixed): two settings, `lookupKeys` and `writeKeys` ("" is the app's own: Ctrl+Alt+L / W in the beta). A change re-registers at once; keys another program holds (or Lamha's other shortcut) are refused with Lamha 2's "Another program uses … — it won't work in Lamha." and the old ones stay. A shortcut is Ctrl, Alt or Win with a letter, a digit or F1–F24 (`Hotkey.IsValid`, `Hotkey.Parse`; 13 tests). The tray lists the shortcuts as they are now; the Translate page and Writing tools name the current keys. The clipboard panel's shortcut joins with the panel.
  - **Drafts to approve:** "اضغط المفاتيح الجديدة، أو Esc للإلغاء" / "Press the new keys, or Esc to cancel"; "استخدم Ctrl أو Alt أو Win مع حرف أو رقم أو F1–F24" / "Use Ctrl, Alt or Win with a letter, a number or F1–F24". Keys Lamha's other shortcut already has get the "another program" line for now.
  - **Seen** in both languages and themes, Debug and published: Ctrl+Alt+K set and back to Ctrl+Alt+L (keys sent to the window), Esc cancelling. 377 tests.
- **Answered 2026-10-25 ("go with your recommendation"):** **Delete my data** deletes the API keys and, once everything is closed, the data folder (the database, clipboard history, the rates, the log), then Lamha quits, and the next start begins fresh (with `--profile`, only that folder; seen on a throwaway profile). **What is sent, and where?** opens under its row with Lamha 2's text reworded for the app: the five providers, prices and clipboard history, no page translation. **The drafts above are approved.**
  - **Still to build in Settings:** the Animations Preview; the updates (phase 5).
- **The clipboard panel** (2026-10-25, on `lamha3/clipboard-panel`; `Windows/ClipPanelWindow`, `ViewModels/Panel/ClipPanelViewModel`, `Services/ClipPanelService`): the signed-off design, Lamha 2's panel.js. Ctrl+Alt+V in the beta (a third shortcut, `clipboardKeys`, changeable in Settings → Keyboard shortcuts) opens it under the mouse over the program in front, in the card's kind of window (Acrylic or solid as the card's setting, 380 × 460); pressed again it closes, and clicking elsewhere closes it. **The list** is the Clipboard page's (`ClipboardViewModel` in its panel mode): search ready to type, All / Pinned, the newest copy chosen, the copies numbered 1–9; Enter pastes into that program (Shift+Enter as plain text, Ctrl+1…9 that copy, a click too), the copy keeping its formatting (Windows' "HTML Format", `Clipboard.WriteClip`) and staying on the clipboard as with Win+V, and it moves up as used; Ctrl+P, Delete with Undo, Esc closes. **Tab** (or → with nothing searched) opens the copy's tools, numbered, with what each does (Lamha 2's hints), 1…9 runs one; a result shows before it's pasted (Enter pastes it, Copy, Redo, Esc back to the tools, Esc again to the list); Look up opens the card on the copy, whose Replace then goes to the same program. **Paused** shows the banner and Resume; **not turned on yet**, what it keeps and Turn on. From the tray's new Clipboard item there's no program to paste into: a paste copies, and says "Copied · Paste it yourself with Ctrl+V" (Lamha 2's). Settings → Clipboard now starts with Lamha 2's intro line naming the keys.
  - **Search marks what it matched** (Lamha 2's `highlight()`, Core's `SearchText.MatchRanges`, `Controls/Marked`), in the panel and on the Clipboard page; a new token, `LamhaMarkBrush` (the design's amber; Windows' highlight colours in high contrast). Typing chooses the first match (Lamha 2's).
  - **All the words are Lamha 2's;** one reworded in its markup only: "Back to the list" without its arrow (the button's icon is the arrow).
  - **The window is kept** between openings, like the card: made afresh each time it saves nothing (commit stays at 88 MB either way, WinUI staying loaded), and kept it reopens in 15–31 ms instead of about 200.
  - **Seen** in both languages and themes, Debug and published: off, Turn on, the list, search in English and Arabic (الاحد finds الأحد), Tab and the tools, Translate's result, Esc back twice, paused, Resume, Ctrl+P, Delete and Undo, and the real flow into a test window (the shortcut over it; Enter, Ctrl+2 and a translation's Paste each pasted there). `--panel` opens it at start for screenshots. **Measured** (NativeAOT): panel open 40 / 90 MB, closed 4 / 91; with the panel used once, the card's stages rise 3–5 MB of commit (card open 41 / 91, closed 3 / 88), the main window within its budgets (26 / 96, 4 / 94). Over the card's 85 MB commit budget, as before but by more: the earlier question. 382 tests.
  - **For you to try on the desktop:** pasting into your programs (Word keeps the formatting; a terminal gets nothing typed, as with Replace), and the panel's Look up → card → Replace.
- **First run** (2026-10-26, on `lamha3/first-run`; `Windows/FirstRunWindow`, `ViewModels/FirstRun/FirstRunViewModel`, `Services/FirstRunService`): the signed-off design, 640 × 540, Mica, minimise and close, the steps in a row at the top (done ones checked). Shown at the first start only: Lamha 2's `installed.flag` (main.js), now in the data folder, so Delete my data brings first run back and a backup doesn't carry it; a data folder whose database was already there (the betas before this) is marked done without showing it. Closing the window in any way ends first run; quitting Lamha during it doesn't. Afterwards, once, the tray design's "Lamha is by the clock" notification, with the lookup keys in use.
  1. **Language:** Arabic or English as choices, each in its own name. Choosing one saves it and **switches at once**: the window is made again in that language and direction where it was, and the card, the clipboard panel and the tray's words follow (Lamha's strings are read afresh; the card's `x:Uid` names follow too).
  2. **Try it:** the sample sentence, selectable; the lookup shortcut, while this window is in front, opens **the real card** on the word selected there, with the sentence around it (so "settle" shows "in this context: تسوية" and the sentence translated), and "That's it" replaces the instruction. With nothing selected, the card's own hint. The writing and clipboard shortcuts named under it, all three as they are now; Customise shortcuts opens Settings → Keyboard shortcuts, and the keys are read again when the window comes back to the front.
  3. **Writing tools:** Settings' own five providers (now one control, `Views/WritingSetupView`, in both places), so a key is checked and saved, and Ollama is found, as in Settings; the step scrolls. **Later** puts back the provider saved when the step opened (no AI until the user chooses).
  4. **Ready:** Start with Windows (on) and clipboard history (as it is: off on a new install); **Start** applies both and closes the window. With `--profile`, Start with Windows isn't written (the real Run key is left alone).
  - **Not built yet: Lamha 2's data** (step 2 of the design). It needs the importer, which is phase 6; the step joins then, between the language and Try it.
  - **Differences from the design:** the card on Try it is the real one, opening by the mouse, so the sample box holds only the sentence (the design drew the card inside it); Windows draws a greyed maximise button beside minimise and close, as it does for every window that can't be maximised.
  - **Found while building it:** Settings → Writing tools' intro ("Select any text in any program and press Ctrl+Alt+W…") never showed: bound once, it was read before the page set it. Fixed.
  - `--first-run` shows it at start even when done (`--first-run=try`, `writing` or `ready` at that step), for screenshots; it never shows with `--measure`, `--page`, `--panel` or `--sample`.
  - **Seen** in both languages and themes, Debug and published, through UI Automation on fresh profiles: each step, the word selected and the shortcut pressed (the real card, Arabic, then English after switching), Later, clipboard history turned on and Start (the flag written, the setting saved), a second start (no first run), and a database without the flag (none, flag written). **Measured** (NativeAOT): first run open 37 / 87 MB, on Writing tools 41 / 92, after Start 4 / 91 (as the main window closed); the budget stages alternating with `main`'s build are the same (tray 1 / 55–57, card 27 / 86–89: the earlier question). 382 tests.
  - **For you to try on the desktop:** first run from a real first start (`--profile=` an empty folder from Explorer, or Delete my data on a copy), and Start with Windows set by Start.

## Moving users from Lamha 2

- **First run of 3.0** finds `%APPDATA%\Lamha` and imports settings, history, the deck (with `mod` and `cardsRemoved`), the mistake journal and clipboard history.
  - API keys and clipboard history are encrypted by Electron's `safeStorage`: DPAPI protects a key kept in `Local State`, and the data is AES-GCM. Both can be decrypted by the same Windows user. If that fails, Lamha asks for the key again.
- **Kept where they are:** language packs (re-downloaded in the new format when needed) and downloaded Wikipedia files (left untouched for when offline Wikipedia returns).
- **The bridge:** a last 2.x update tells users about 3.0 and installs it. electron-updater can't install a Velopack app, so it downloads and runs 3.0's setup, which imports the data and then removes 2.x.
- **The Firefox extension:** a last version on addons.mozilla.org saying it's ended and pointing to the Windows app, then unlisted. Publishing that is your call when the time comes.

**Questions**
- **Remove 2.x automatically, or leave it for the user?** Recommendation: remove it after a successful import, but keep its data folder for 30 days so nothing is lost if something went wrong. *(phase 6)*
- **Settings that no longer exist** (per-site switches, the Firefox link): drop them silently? Recommendation: yes, and list them in the import summary so nothing seems to vanish. *(phase 6)*

## Risks

| Risk | What we do |
|---|---|
| **The card stealing focus** breaks Replace / Insert in the other program | A non-activating window (`WS_EX_NOACTIVATE`) with focus handled the way `native.js` does it today. Prototyped in phase 0, before anything else is built on it. |
| UI Automation calls hanging on a frozen program | Its own thread, a timeout, and no sentence when it times out (as today). |
| A library that doesn't work with NativeAOT | Each package is checked in phase 0. Plain trimmed publishing (not AOT) is the fallback. |
| "OpenAI-compatible" servers that differ in small ways (streaming format, model lists, error shapes) | Test against LM Studio, OpenRouter and Groq in CI with recorded answers; tolerant parsing. |
| Google's unofficial endpoints changing | The same risk as today, with the same AI fallback. |
| Drifting from 2.x behaviour | Ported tests plus the parity tool. |
| SmartScreen warnings for an unsigned installer | Code signing: your decision, independent of this plan. |

## Future: offline Wikipedia and the reader

Held back from 3.0 so the first release has no web view. Ideas for doing it better than 2.x when it returns:

- **A native reader, no web view:** convert the cleaned article (paragraphs, headings, lists, links, pictures) to XAML `RichTextBlock`s. That's what Lamha shows most, at a fraction of WebView2's ~100 MB. Tables and formulas are the hard part: show simple tables natively, and offer "open in the browser" for the rest.
- **Or a web view in its own process,** started only for the reader and ended when it closes, so all its memory comes back. A fallback if the native reader can't handle enough articles.
- **Title search in SQLite (FTS5),** built once per downloaded file, so finding an article is instant and doesn't keep a ZIM index in memory.
- **Downloads through Windows' BITS service,** which resumes across restarts and slows down when the user needs the network.
- **Smaller downloads:** offer Kiwix's "nopic" and topic sets first, with the size and what's in each in plain words.

**Questions (for when it returns)**
- Native reader, web view, or both?
- Keep the 2.x ZIM reader's rules (Wikipedia files only, ZIM 6+, no old xz files)?

## Questions by phase

Phase 0's four and the two it raised, phase 2's four, phase 1's four, phase 3's two and phase 4's are answered (see each section). The first GitHub pre-release comes right after phase 4's card. The rest:

| Phase | Question | Recommendation |
|---|---|---|
| 6 | Code signing? | Yours, once everything else is done (the beta ships unsigned until then) |
| 6 | Warn users with downloaded Wikipedias? | Yes, in the bridge update |
| 6 | Remove 2.x automatically? | Yes, keep its data for 30 days |
| 6 | Drop settings that no longer exist? | Yes, listed in the import summary |

## What's needed from you

- **Answers to the questions above** by the phase each one names. Phase 4's come next.
- **Trying the prototype** on your desktop (phase 0's one unverified step).
- **Sign-off on the screen designs** (phase 1) and on any copy changes.
- **Testing the betas on your PCs**, especially Arabic input and the programs you use Lamha in.
- **Decisions when they come up:** the Firefox extension's last version, and code signing once everything else is done.

Tools: Visual Studio 2026 (or the .NET 10 SDK with the Windows App SDK workload), Windows 11, and Developer Mode on for running the app from source.
