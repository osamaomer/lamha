# 0017. Language packs are SQLite files, converted from Lamha 2's and published as packs-v2

**Accepted, 2026-10-26, from phase 5's language packs (the user's call). Extends 0006 (SQLite for the dictionary) to the packs.**

## Decision

- **A pack is one read-only SQLite file** (`Lamha.Data/Dictionary/PackFile`, format 2): `meta` (format, lang, version, words…), `words` (headword → its entry's JSON, the dictionary's shape without the Arabic) and `forms` (inflected form → headword). Opened when Lamha's lookups start, read from disk at each lookup, as the offline dictionary is.
- **Converted, not rebuilt:** `tools/DictionaryBuilder pack` turns Lamha 2's packs (`packs-v1`, gzip'd JSON from legacy/tools/build_packs.py) into these files. The words are the same; only the way they're kept changes.
- **Published as `packs-v2`** (`<lang>.db.gz`) by `.github/workflows/packs.yml`, started by hand, never marked latest. Lamha 2 keeps downloading `packs-v1`.
- **Kept in the data folder's `packs`** (`fr.db`…). A download is written as `fr.db.part`, unzipped as it comes, and checked (SQLite's `quick_check`, the format, the language) before it takes the old file's place, so a download cut short or damaged never replaces a pack that works. Delete my data takes them with it.

## Why

- **Memory:** Lamha 2 read a pack's JSON whole to install it and kept its most recent 80 parts (shards) in memory. A SQLite lookup reads a few pages and keeps a 128 KB cache per pack. Measured with the French pack installed and a French word looked up (NativeAOT): no difference in commit.
- **One way of doing it:** the same reader, builder and rules as the dictionary (0006).
- **Not rebuilt from Wiktionary:** the words Lamha 2's users have are the ones tested; rebuilding today would change the vocabulary for no reason. The workflow can rebuild (its `rebuild` choice) when that's wanted.

## Turned down

- **Reading Lamha 2's `.json.gz` files:** no new release to publish, but the whole file is parsed at every install (the French one is 14 MB of JSON), and the shards in memory or on disk as JSON need a cache of their own.
- **The `.db` files ungzip'd on the release:** about three times the download (French 17 MB instead of 5.9 MB).

## Consequences

- Downloads are about 12% bigger than Lamha 2's (French 5.9 MB, Turkish 2.1 MB), and the files on disk three times that (French 17 MB).
- A new pack format is a new release (`packs-v3`) and a new `PackFile.Format`; a file in another format is left unused and downloaded again from Settings.
- Lamha 2's users' packs aren't converted on this PC: the importer (phase 6) can download the new ones for the languages they had.
