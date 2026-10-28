# 0022. The reader: books as Lamha's own text, in a window of their own process

**Accepted, 2026-10-28, the user's calls (EPUB first; comics and their translation planned; a catalogue of free books). Built from stage 1 of native-plan.md's *Future: Lamha Reader*.**

## Decision

- **A book opens in its own window, in its own process** (Lamha started again for that book), so closing it gives all its memory back: WinUI never frees a closed window (decision 0021), and a book's pictures and pages are the largest thing Lamha would hold. The library is the main window's Read page.
- **No web view.** Core reads the book (`Lamha.Core.Reading`) and the page shows it as WinUI text (`RichTextBlock`):
  - **flowing text** (EPUB, TXT, DOCX): each part becomes *blocks* (heading, paragraph, list item, quotation, picture, line) of *runs* (italic, bold, a link). What the markup means is kept; the book's CSS and fonts are not, since the page sets the text in the reader's own font, size and colours;
  - **fixed pages** (comics, then PDF): a picture of each page plus *text regions*, each a box with its text, so the card, Review and translation work on a speech bubble as on a sentence, and translating a comic in place is a region's translation drawn over its box.
- **Lamha keeps its own copy of each book** in its data folder; highlights, notes, bookmarks and the place are in Lamha's database, tied to the book by its hash.
- **Only books without DRM open** (`book_locked`); the font obfuscation EPUBs use is not DRM and is ignored.
- **Read safely:** the ZIP's paths are resolved inside the book only (`..` never climbs out); XML is read without DTDs or external resolution, HTML's named entities replaced first; a part or package file over 16 MB is refused (`book_bad`).

## Why

- **Memory:** WebView2 costs ~100 MB and 3.0 shipped without one (decision 0005). Native text is a fraction of that, and the reader's own process takes even that back when the book closes.
- **Lamha knows the words:** the card needs the word, its sentence, its paragraph and the book. In its own text it has them exactly, which UI Automation in another reader can't always give (PDF readers least of all).
- **One reader for later:** offline Wikipedia needs the same native page, and fixed pages with text regions serve comics, PDF and comic translation alike, so each is built once.
- **Measured on real books** (Pride and Prejudice, 2026-10-28): Project Gutenberg's illustrated EPUB 3 (25 MB, 130,000 words, 165 pictures) is read part by part in 0.16 s in all; Standard Ebooks' (121,000 words) in 0.08 s.

## Consequences

- The page can't show what a book does only with CSS (drop caps, a poem's exact indents, fixed-layout EPUBs). Simple tables are kept as rows of cells; anything richer may later get "open in the browser".
- The reader's process is a second Lamha: it has to share the database and the settings with the main one, and the card it opens is its own.
- New error codes: `book_bad`, `book_locked`.
