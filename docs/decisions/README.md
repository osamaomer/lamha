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
