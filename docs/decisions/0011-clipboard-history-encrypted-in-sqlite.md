# 0011. Clipboard history in SQLite, each copy encrypted for the Windows user

**Accepted, 2026-10-24. Built in phase 3.**

## Decision

- Clipboard history is the `clips` table of `lamha.db` (decision 0006). What was copied (text, formatting, label, cached results) is one JSON body per copy, encrypted with DPAPI for the current Windows user (`Lamha.Windows.Security.Dpapi`, with Lamha's own entropy). Only what sorting and "programs" need is plain: times, counts, pinned, the program's file name and the language tag.
- The same text copied again is found by an HMAC-SHA256 of the trimmed text, keyed with 32 random bytes that are themselves DPAPI-encrypted in the `state` table. Nothing is decrypted to record a copy.
- Copies are decrypted only while the history is being looked at (search needs their text); `ClipboardHistory.Unload()` drops them when the history closes. A copy this account can't decrypt is skipped, not deleted.
- The listener and its rules are Lamha 2's (`ClipCapture`: off or paused, the programs' own "don't keep this" formats, excluded programs never read, bank-card numbers, 200,000 characters, 500 KB of HTML), in the same order.

## Why

- **Memory.** Lamha 2 kept every copy in memory and rewrote one encrypted JSON file; with 1,000 copies of up to 200,000 characters that's unbounded. In the tray, Lamha 3 now holds no copy at all.
- **Privacy, as before.** Lamha 2 encrypted the whole file with DPAPI (Electron's safeStorage). Per-copy encryption keeps that: another Windows account, or a copied disk, reads nothing but times and program names. A plain hash would let someone with the file confirm a guess ("did they copy this password?"); the keyed HMAC doesn't.

## Turned down

- **One encrypted file, as in Lamha 2:** everything in memory again, and every change rewrites it all.
- **SQLite's own encryption (SQLCipher):** a native dependency for one table, and its key would still need DPAPI.
- **A plain search index:** fast, but it's the copies' text in the clear.

## Consequences

- Searching 1,000 copies stays under 16 ms after the first look, which decrypts them (Lamha 2's test, kept).
- The importer (phase 6) decrypts Lamha 2's `clipboard.json` (safeStorage) and writes the copies through `ClipboardHistory`.
