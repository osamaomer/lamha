# 0006. SQLite for settings, data and the dictionary

**Accepted, 2026-10-24. Built in phase 2. Amended 2026-10-24: the data has its own folder (to build before the first beta release).**

## Decision

- Settings, history, the review deck, the mistake journal and clipboard history live in one SQLite database in `%LOCALAPPDATA%\Lamha Data` (`Microsoft.Data.Sqlite`), with versioned schema migrations. The log goes there too.
- The offline dictionary (Lamha 2's 22 MB of JSON shards) and the language packs become read-only SQLite files, built in CI.
- API keys are never in the database: they go to Windows Credential Locker (or DPAPI).

## Why

Lamha 2 read and rewrote whole JSON files and kept the deck in memory. SQLite answers queries from disk, so data doesn't grow memory, and it writes safely when the app is stopped mid-write.

The data first lived in `%LOCALAPPDATA%\Lamha`, but that's Velopack's install folder (decision 0004), and uninstalling Lamha deletes it, the review deck with it. A folder of its own survives an uninstall or a reinstall; deleting the data becomes a choice in Settings.

## Consequences

- A `Lamha.Data` project (phase 2), with the importer for Lamha 2's `%APPDATA%\Lamha` files.
- `legacy/tools/build_dict.py` and `build_packs.py` get a SQLite output.
