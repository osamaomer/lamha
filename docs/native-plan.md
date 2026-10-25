# Lamha 3: the native Windows app

The plan for rebuilding Lamha as a native Windows 11 app (WinUI 3 + Windows App SDK) and dropping the Firefox extension and the Linux plan. Written 2026-10-24.

**Where it stands:** phases 0 and 1 are done ([phase 0](#phase-0-results), [phase 1](#phase-1-progress)). Phase 2 (the brain) is built but for the parity tool ([progress](#phase-2-progress)), and phase 3 (Windows integration) is built ([progress](#phase-3-progress)). Phase 4 (the UI) is under way: the card is done and out as the first beta ([progress](#phase-4-progress)).

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
   | Main window open | ~155 · ~313 MB | not built yet (its frame: 18 · 86 MB) | **90 / 110 MB** (to confirm in phase 4) |
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
- **Next:** Translate + Today, then History, Write, Review and the Clipboard page.

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
