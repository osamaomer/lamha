# Decision records

One short file per decision that shapes Lamha 3: what was chosen, why, and what was turned down. A decision that
changes gets a new record that replaces the old one (the old one says so at the top); records are never rewritten.

| # | Decision | Status |
|---|---|---|
| [0001](0001-native-winui3-csharp.md) | A native app: WinUI 3 in C# / .NET 10 | Accepted, 2026-10-24 |
| [0002](0002-windows-11-only.md) | Windows 11 only; no Firefox extension, no Linux | Accepted, 2026-10-24 |
| [0003](0003-repository-layout.md) | One repository: `main` is Lamha 3, Lamha 2 in `legacy/` and on `release/2.x` | Accepted, 2026-10-24 |
| [0004](0004-velopack-github-releases.md) | Velopack installer and updates from GitHub Releases | Accepted, 2026-10-24 |
| [0005](0005-no-web-view-in-3-0.md) | No web view in 3.0 | Accepted, 2026-10-24 |
| [0006](0006-sqlite-storage.md) | SQLite for settings, data and the dictionary | Accepted, 2026-10-24 (built in phase 2; its own data folder, amended 2026-10-24) |
| [0007](0007-openai-compatible-client.md) | One OpenAI-compatible client for OpenAI and custom servers | Accepted, 2026-10-24 (built in phase 2) |
| [0008](0008-memory-budgets.md) | Memory budgets on commit and Task Manager; idle memory handed back | Accepted, 2026-10-24 |
| [0009](0009-card-window-kept.md) | The card window is kept hidden between lookups | Accepted, 2026-10-24 |
| [0010](0010-ai-over-own-http.md) | The AI providers over Lamha's own HTTP, not their SDKs | Accepted, 2026-10-24 (built in phase 2) |
| [0011](0011-clipboard-history-encrypted-in-sqlite.md) | Clipboard history in SQLite, each copy encrypted for the Windows user | Accepted, 2026-10-24 (built in phase 3) |
| [0012](0012-sentence-through-ui-automation-in-process.md) | The sentence around a word: UI Automation in Lamha's process, on its own thread | Accepted, 2026-10-24 (built in phase 3) |
| [0013](0013-main-window-memory.md) | The main window's memory: closed after use, .NET's spare memory given back, 95 MB after it for now | Accepted, 2026-10-25 (provisional until its pages are built) |
| [0014](0014-notifications-windows-toasts-and-lamha-links.md) | Notifications: Windows' own toasts, their clicks as Lamha's links | Accepted, 2026-10-26 (built in phase 4) |
| [0015](0015-card-memory-budget.md) | The card's commit budget: 95 MB, set from what the memory is | Accepted, 2026-10-26 (replaces 0008's 85 MB for the card) |
| [0016](0016-speech-through-the-wave-player.md) | Listen plays through Windows' wave player (MP3 decoded by Windows' codec), not Media Foundation | Accepted, 2026-10-26 (built in phase 5) |
| [0017](0017-language-packs-as-sqlite-files.md) | Language packs are SQLite files, converted from Lamha 2's and published as packs-v2 | Accepted, 2026-10-26 (built in phase 5) |
| [0018](0018-importing-lamha-2.md) | Lamha 2's data moves at first run, read in place (safeStorage decrypted in Lamha) and merged as a backup is | Accepted, 2026-10-26 (built in phase 6) |
| [0019](0019-the-bridge-update.md) | The bridge: Lamha 2.1.0 offers Lamha 3 and starts its setup; Lamha 3 removes Lamha 2 | Accepted, 2026-10-26 (built in phase 6: 2.1.0, and Lamha 3's removal) |
| [0020](0020-the-design-system.md) | One design system, with Windows' Settings as the reference: its measurements, Fluent icons, Windows' brushes and motion | Accepted, 2026-10-26 (signed off; built from step 2 of docs/design/system-plan.md) |
| [0021](0021-main-window-kept.md) | The main window is hidden, not closed: WinUI never frees a closed window | Accepted, 2026-10-27 (replaces 0013's closing; 105 MB after it) |
