# CLAUDE.md: working on Lamha 3

Claude Code loads this file at the start of every session. Keep it short. The plan is [docs/native-plan.md](docs/native-plan.md), the decisions are in [docs/decisions/](docs/decisions/), and Lamha 2's history is in [docs/history.md](docs/history.md).

## What Lamha is

An Arabic-first dictionary, translator and English writing coach for Windows 11 (the user's own project; the user is Arabic-speaking and writes the product copy in Arabic and English). **Lamha 3** is a native rewrite: WinUI 3 + Windows App SDK, C# / .NET 10, NativeAOT, installed with Velopack. It replaces Lamha 2 (Electron + a Firefox extension), whose code is in `legacy/` as the reference for the port; `legacy/CLAUDE.md` describes it.

**Status:** phase 0 (foundations) is done. **Phase 2 (the brain) is mostly built** on `native/phase-2`: Core and Data with Lamha 2's tests ported (see the plan's *Phase 2 progress* for what's left). **Phase 1** (design system and screen designs, with the user's sign-off) runs alongside. The open questions per phase are at the end of the plan.

## Layout

| Path | What |
|---|---|
| `src/Lamha.Core` | The brain, no Windows dependency: `Translation/` (lookup, Google, the AI translator, the router), `Ai/` (the five providers), `Dictionary/`, `Writing/`, `Review/` (deck, Today, history, backups), `Wikipedia/`, `Diagnostics/`, `Settings/`, `Abstractions/` (the storage interfaces), `Text/`, `Net/` |
| `src/Lamha.Windows` | Win32 through CsWin32 (`NativeMethods.txt`): `Shell/` (message window, tray icon, foreground window, screens, memory), `Input/` (hotkeys, keyboard), `Clipboard/`, `Selection/` |
| `src/Lamha.App` | WinUI 3: `Program.cs` (Velopack, single instance), `App.xaml.cs` (composition root), `Windows/`, `ViewModels/`, `Services/`, `Themes/Tokens.xaml`, `Strings/ar|en/Resources.resw`, `Assets/` |
| `src/Lamha.Data` | SQLite: `Database/` (the user's database: schema steps in `Migrations.cs`, the stores) and `Dictionary/` (the read-only dictionary file and its builder). The Lamha 2 importer comes in phase 6 |
| `tests/` | xUnit v3 on Microsoft.Testing.Platform, one project per `src` project. Core's tests run the brain as it runs (`Support/TestLamha`: real SQLite stores in memory, a fake network that records requests, a fake clock), like Lamha 2's `makeEnv` |
| `tools/DictionaryBuilder` | Builds the dictionary file from Lamha 2's JSON shards (`legacy/dict`) |
| `tools/Measure/measure.ps1` | Memory, measured from outside like Task Manager, against the budgets |
| `legacy/` | Lamha 2, unchanged: run its tests with `npm test` inside it |

Layers: App → Windows, Data → Core. Never the other way. Core never references Windows or WinUI.

## Rules the code follows

- **Match the style:** `.editorconfig` is enforced by the build, and warnings are errors. File-scoped namespaces, `_camelCase` private fields, braces on multi-line blocks, short "why" comments in plain English, XML doc comments on public types.
- **NativeAOT-safe only:** no reflection-based serialization (use `System.Text.Json` source generation, or `JsonNode` for request bodies), MVVM through CommunityToolkit.Mvvm's source generators, `x:Bind` (not `Binding`), CsWin32 without marshalling. A new package must publish with AOT without warnings (the official Anthropic SDK doesn't: decision 0010).
- **Package versions** live only in `Directory.Packages.props`. Only the Windows App SDK parts Lamha uses (WinUI, Foundation, InteractiveExperiences), not the metapackage (it adds ~60 MB of AI and ML).
- **Strings:** every visible string is in both `Strings/en/Resources.resw` and `Strings/ar/Resources.resw`; never hard-coded. Strings from Lamha 2 keep its wording, with the old key as the resw comment. `Proto_` strings belong to the prototype and go with it.
- **Right to left:** every window's root gets `Localization.FlowDirection`.
- **Privacy:** the log (`Services/AppLog.cs`, `%LOCALAPPDATA%\Lamha\logs\lamha.log`) holds events, lengths and exception *types*, never user text, keys, clipboard contents or exception messages. Lamha's own clipboard writes for a paste are kept out of Windows' clipboard history (`Clipboard.WriteText(..., keepOutOfHistory: true)`); the user's Copy is not.
- **Memory:** nothing stays alive in the tray but the tray icon, hotkeys and (later) the clipboard listener. Windows tell `IdleMemory` when they open and close. Run `tools/Measure/measure.ps1 -Budget` after anything that could change memory; budgets are in `docs/decisions/0008-memory-budgets.md`.
- **Errors** are `LamhaException` codes (Lamha 2's: "network", "ai_bad_key"…); the interface turns codes into words, the log keeps only codes.
- **Porting from Lamha 2:** port behaviour test by test. A rule taken from `legacy/` names its source in the doc comment (e.g. "Lamha 2's `isLookupCandidate()` in background.js") and keeps its numbers.
- **Decisions:** a choice that shapes the architecture gets a record in `docs/decisions/` (and a line in its README).
- **Versions:** `VersionPrefix` / `VersionSuffix` in `Directory.Build.props`.

## Working with the user

- They care a lot about **alignment and spacing**: check edges and gaps in every new or changed screen, in both languages and both themes.
- Product copy is **their own words**: ask before rewriting it, and reuse Lamha 2's strings where they fit.
- They often report bugs with screenshots or GIFs; reproduce from the picture before guessing.
- They asked to be offered **ideas and questions along the way**: the plan has both per phase.

## This PC, and Claude's shells here

- **Lamha 2 is installed and running** (`C:\Program Files\Lamha\Lamha.exe`, also named `Lamha.exe`, several processes). Never stop or read processes by name; use the process you started. The beta's shortcuts are Ctrl+Alt+L / W / V so both can run.
- **Claude's shells have no clipboard** (even PowerShell's `Set-Clipboard` fails, unsandboxed and in the Terminal panel too). The clipboard tests skip there with a reason; they run in CI and on the user's desktop. An app started from these shells can't use the clipboard either, so the real shortcut → card → Replace flow must be tried by the user (start the app from Explorer).
- **NativeAOT publish** needs `vswhere.exe` on PATH: `$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;" + $env:PATH`. ARM64 needs Visual Studio's C++ ARM64 build tools, which this PC doesn't have (CI does).

## Commands

```powershell
dotnet build Lamha.slnx                                   # everything (Debug)
dotnet test --solution Lamha.slnx                         # all tests
dotnet test --project tests/Lamha.Core.Tests              # one project
dotnet run --project src/Lamha.App                        # the app, Debug (tray icon; Ctrl+Alt+L)
dotnet publish src/Lamha.App -c Release -r win-x64 -o artifacts/publish/win-x64   # NativeAOT
./tools/Measure/measure.ps1 -Budget                       # memory of the published build
dotnet run --project tools/DictionaryBuilder -- legacy/dict artifacts/dict/lamha-dict.db   # the dictionary file
dotnet tool restore; dotnet vpk pack ...                  # the installer: see .github/workflows/ci.yml
```

Before every commit: the whole build (0 warnings) and `dotnet test --solution Lamha.slnx`; for `legacy/` changes, `npm test` inside it.

## Sessions and models

- **One task per session.** When the user starts something unrelated, suggest `/clear` in one line before starting.
- **Model:** routine work (strings, XAML spacing, a known fix, docs, tests) suits Sonnet; anything crossing layers, Win32 and focus, the port of the deck or translation service, async races, security, design work and new features suits Opus. Say so in one line only when the session's model doesn't fit; the user types `/model`.

## Compact instructions

When compacting, keep: the task and the user's decisions, files changed and why, test and measurement results, and what's left. Drop file contents that were only read, and passing test lists.
