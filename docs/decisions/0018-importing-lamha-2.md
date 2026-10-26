# 0018. Lamha 2's data moves at first run, read in place and merged as a backup is

**Accepted, 2026-10-26, from phase 6's importer. The questions it answers were the user's (2026-10-26, as recommended): settings Lamha 3 doesn't have are listed after the move, Lamha 2's `enabled` isn't moved, and 2.x is removed after a successful import with its folder kept for 30 days.**

## Decision

- **When:** first run's step 2, right after the language, only when `%APPDATA%\Lamha` has Lamha 2's storage files. The step lists what moves, counted; **Move my data** moves it, **Start fresh** leaves it. Nothing in Lamha 2's folder is changed either way.
- **Read in place, in Lamha's process** (`Lamha.Data/Import/Lamha2Folder`): storage-sync.json and storage-local.json (desktop/storage.js), clipboard.json and wikipedia.json. The API keys and clipboard history were encrypted by Electron's safeStorage; they're decrypted as Chromium does it (`ElectronSafeStorage`): the AES-256 key in `Local State`, sealed by DPAPI for this Windows user (`Dpapi.UnprotectForUser`, no entropy), then each value `v10` + nonce + AES-GCM. Nothing of Electron's is needed.
- **Moved by Core** (`Lamha.Core/Import/Lamha2Import`), into the stores Lamha 3 already has:
  - **The deck, its removals, the history, the mistake journal, the days' practice and the lookup count** through the backup's own merge (`BackupService.RestoreAsync`): Lamha 2's storage.local has a backup's keys. Nothing already in Lamha 3 is lost, and moving twice adds nothing.
  - **The deck's counters** (`cardStats`: the streak goes on) and whether Lamha 2 had made its history into cards (`cardsImported`).
  - **Settings**, each only when it's there and of its kind (an unknown value keeps Lamha 3's default). **API keys** into Credential Manager, unless Lamha 3 already has one for that provider. Write's **draft** and the clipboard summary's language.
  - **Clipboard history** with each copy's id, times, pin, label, translations and summaries (`ClipboardHistory.Import`), re-encrypted as Lamha 3 keeps it (0011). Proofreading results aren't kept: they're made again when asked.
- **Not moved:** the interface language (step 1 has just asked), `enabled` (the user's call), `windowMica` (off by default in Lamha 2; Lamha 3 is designed on Mica), and what was Lamha 2's own bookkeeping (`cardsRev`, `wotd`, `errorLog`, `motionHint`, `popupMode`…).
- **Named after the move** when this user had them, so nothing seems to vanish: Lamha in Firefox (any of its settings stored) and offline Wikipedia (files downloaded; they stay where they are). A key or clipboard history Windows can't decrypt (the folder came from another account) is said, and the key is asked for again.

## Why

- **In place, not exported by Lamha 2:** the bridge update can't run Lamha 2's code after Lamha 3 is installed, and users who skip it (or install 3.0 by hand) still have their data. Lamha 2's files are plain JSON, and the decryption is two documented steps.
- **One merge:** the backup's merge is ported test by test from Lamha 2 and already handles a deck that isn't empty, removals and untrusted files.
- **In Core and Data:** the files and the decryption are disk (Data); what each value means is the brain's (Core), tested on the real stores with `TestLamha`.

## Turned down

- **Asking Lamha 2 to export a backup first:** it covers only the deck, history, journal and practice (not settings, keys or clipboard), and needs Lamha 2 running.
- **Copying Lamha 2's files into Lamha 3's folder to read later:** a second copy of the keys on disk, for nothing.

## Consequences

- Lamha 2's folder is only read. **Removing 2.x** after the import, and its folder after 30 days (its downloaded Wikipedias kept), comes with the bridge update, in the released 3.0: a beta never removes Lamha 2, since both run side by side until then.
- Lamha 2's language packs aren't converted; the languages they had can be downloaded again from Settings → Without internet (0017).
- A Lamha 2 setting added after this (on `release/2.x`) isn't moved until it's mapped here.
