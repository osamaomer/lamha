# Lamha's roadmap

Where Lamha is going, and how to add to it. Nothing below is a promise: the **Next** list is planned, the **Ideas** wait for a decision, and the **Add-ons** part says where new things plug in.

Each item says where its details live. The day-to-day plan with its history is [native-plan.md](native-plan.md); decisions are in [decisions/](decisions/).

## Coming in 3.1 (on `main`, not released yet)

The design system release: every screen measured against Windows' own Settings, and a few new things.

- **Every screen on one system:** Windows' controls, sizes and motion, with Lamha's colours and its own pane icons ([design/system.md](design/system.md)).
- **The mini translator:** a small Lamha that floats over what you're reading.
- **Side by side:** a paragraph on the Translate page shows in two columns, the original and its translation.
- **Paste and translate:** the empty Translate box offers what you just copied.
- **The jump list:** right-click Lamha in the taskbar for Translate, Write, Review and Clipboard.
- **The card:** pin it and it stays while you look up more words; Settings opens without closing it; no tooltips in the way.
- **The main window:** Lamha's icon and version where Settings shows your account; a title bar and search like Settings'.

## Next

| What | Why | Details |
|---|---|---|
| **Lamha Reader** | Books read with Lamha's card, translation and Review in the page; EPUB first, PDF later | [native-plan.md → Future](native-plan.md#future-lamha-reader): the reading page signed off ([design](design/reader.html)); the library drafted, waiting for sign-off ([design](design/library.html)) |
| **Offline Wikipedia and the reader** | Whole articles with no internet; held back so 3.0 shipped without a web view | [native-plan.md → Future](native-plan.md#future-offline-wikipedia-and-the-reader): a native reader, title search in SQLite, downloads through BITS |
| **FSRS reviews** | Anki's newer scheduler: fewer reviews for the same memory | A choice in Settings → Review, beside today's SM-2 |
| **A second example on the review card** | So a meaning doesn't stick to one sentence | From the dictionary, on the back |
| **Export to Anki** | For people who already have a routine there | `.apkg` or CSV |
| **Practice from the mistake journal** | Turns "you often miss *the*" into a short quiz with your own sentences | Built from the journal's mistake kinds |
| **"What's new in 3.1"** | Points at the new pane and Back the first time after the update | A TeachingTip |
| **Interface sounds** | Windows' own, off by default as in Windows | WinUI's `ElementSoundPlayer` |
| **The parity tool** | Runs Lamha 2's brain and Lamha 3's side by side on the same words | Started on the `lamha3/parity-tool` branch |
| **Code signing** | No SmartScreen warning on the installer | The owner's call (about $10 a month, or a Microsoft Store listing, which signs it) |

## Later: removing Lamha 2's code

No one uses Lamha 2 (the user, 2026-10-27), so what Lamha 3 carries for moving from it can go. Kept for now; one task when we come back to it:

| What | Where | What goes with it |
|---|---|---|
| **The importer** (first run's Lamha 2 step) | Core's and Data's `Import/`, first run's step | [Decision 0018](decisions/0018-importing-lamha-2.md) marked replaced; first run one step shorter |
| **Removing Lamha 2** (first run's offer on Ready, `--remove-lamha2`) | `Services/Lamha2Removal` | [Decision 0019](decisions/0019-the-bridge-update.md)'s Lamha 3 side |
| **The beta's own shortcuts** (Ctrl+Alt+L / W / V, so a beta could run beside Lamha 2) | the shortcuts' defaults, the Beta workflow | Betas take Alt+Shift as releases do |
| **`release/2.x` as the port's reference** | CLAUDE.md's porting rule, `tools/Strings` (`ResourcesTests` checks Lamha 2's wording) | Decide whether the branch stays as an archive or the rule ends |

## Ideas, not decided

### Translating without Google's unofficial endpoint

Sentences and word-in-context use Google's free endpoint, which Google could limit. Single words don't need it (the offline dictionary and language packs), and the AI translator already takes over when Google fails.

1. **Translation packs, offline:** Mozilla's Bergamot (Firefox's own translator, ~20–40 MB per language pair, fast on a CPU) downloaded from Settings like the language packs. Free per use, works with no internet and no Ollama. The order would become Google → local → AI. Measure Arabic quality first: below Google's for idioms. What it gives, the test before any code (Firefox's `about:translations`), how it would be built and the open questions: [native-plan.md → Future](native-plan.md#future-translation-packs-offline).
2. **An official service** for a paid version, with the key on Lamha's server: Microsoft Translator (generous free tier, cheapest after) or Google Cloud Translation. They return only the translation, so the parts of speech come from the offline dictionary.

Prices change: check them before deciding.

### Earning from Lamha

The code is MIT and the dictionary data CC BY-SA 4.0, so what would be sold is convenience or a service, not the code. From least to most work:

1. **Donations:** a "Support Lamha" link (GitHub Sponsors, Ko-fi).
2. **A Microsoft Store listing,** paid or pay-what-you-want: the one-click install with updates. It also signs the app.
3. **Lamha Plus:** the writing tools and AI translation with no API key of your own, through a small server. The likeliest income and the most work: accounts, payments, usage limits, a privacy policy.
4. **Schools and English-teaching centres:** a licence with support, such as a word deck shared by a class.
5. **Grants** for open-source and language-access projects.

Settle the Google question before charging for translation. Not legal or tax advice.

## Left out on purpose

- **The Firefox extension, Linux and Windows 10:** Lamha is a Windows 11 app ([decision 0002](decisions/0002-windows-11-only.md)). Lamha 2, the Electron app and Firefox extension it replaced, is kept for reference on the `release/2.x` branch.

## Add-ons: where new things plug in

Lamha is built in layers (App → Windows, Data → Core; Core never knows about Windows), so most additions touch one place and come with their tests. The rules every addition follows are in [CLAUDE.md](../CLAUDE.md): NativeAOT-safe code, every string in both languages, the design system for anything on screen.

| To add | Start here | Remember |
|---|---|---|
| **An AI provider** | `src/Lamha.Core/Ai/`: implement `IAiProvider` (Claude, Gemini, OpenAI-compatible and Ollama are examples); anything speaking OpenAI's API already works as a custom server | Errors as `LamhaException` codes; the key in Windows' Credential Manager, never in the log |
| **A translation source** | `src/Lamha.Core/Translation/`: `TranslationService` decides the order and the fallbacks | The stray-letter check on AI answers stays |
| **A language pack** | `tools/DictionaryBuilder` (`pack`), published by Actions → Language packs to the `packs-v2` release | [decision 0017](decisions/0017-language-packs-as-sqlite-files.md) |
| **A tool on a copy** (Clipboard page and panel) | `src/Lamha.Core/Clipboard/ClipActions.cs`: `Available()` says which tools fit a copy | Its label in both `.resw` files |
| **A writing tool** | `src/Lamha.Core/Writing/` | The card numbers its tools: 1–9 run them |
| **A Settings category** | `src/Lamha.App/ViewModels/Main/SettingsCategories.cs`, and the Settings page | It's searchable from the title bar by the names listed there |
| **A page in the main window** | `MainViewModel.Pages`, its icon drawn in `Controls/PaneIcon.cs` | Windows Settings' style: one colour, soft shading, both themes |
| **Anything on screen** | The design system first: [design/system.md](design/system.md), a style in `Themes/Controls.xaml`, then the `--gallery` window | `ScreensTests` fails on a typed colour, size, radius, glyph or off-grid gap ([decision 0020](decisions/0020-the-design-system.md)) |
| **Something that stays in the tray** | Think twice: only the tray icon, shortcuts and the clipboard listener live there | Memory budgets in [decision 0008](decisions/0008-memory-budgets.md); `tools/Measure/measure.ps1 -Budget` |

A choice that shapes the architecture gets a decision record in [decisions/](decisions/).
