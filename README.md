<p align="center"><img src="site/icon.svg" width="96" alt="Lamha's icon"></p>

<h1 align="center">لمحة · Lamha</h1>

<p align="center"><b>Select a word. Get it at a glance.</b></p>

<p align="center">An Arabic-first dictionary, translator and English writing coach for Windows 11.<br>
Free, open source, and light enough to forget it's running.</p>

<p align="center"><a href="https://github.com/osamaomer/lamha/releases/latest"><b>⬇️ Download Lamha</b></a> · <a href="#-whats-next">What's next</a> · <a href="#-how-its-built">How it's built</a></p>

---

You're reading an English article and hit the word *bank*. Is it the money kind or the river kind? Select it, press <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd>, and a small card answers next to your mouse:

> *We sat on the river **bank** at sunset.*
> **bank** /bæŋk/ → **في هذه الجملة: ضفّة النهر**
> اسم · الأرض المحاذية لنهر أو بحيرة، وليست «مصرف».

That's Lamha. It reads the sentence around the word, so you get the meaning **in this sentence**, not a list of twelve. It works in any program: WhatsApp, Word, Outlook, Teams, your browser, a PDF.

## ✨ Three shortcuts

| Press | In any program | What happens |
|---|---|---|
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd> | on a word or some text | **Look up and translate**: the card |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> | on your own writing (or nothing, to write new) | **The writing tools** |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> | anywhere | **Clipboard history**, with Lamha's tools on every copy |

All three can be changed in Settings → Keyboard shortcuts. Everything else lives in Lamha's window, one click away in the tray.

## 🔎 The card

**A word** gets:
- its pronunciation and a 🔊 button;
- **its meaning in your sentence**, then its other meanings by part of speech (اسم، فعل، صفة…);
- definitions translated into Arabic, with the English original and an example;
- synonyms you can follow (← and → take you back and forth);
- a Wikipedia summary, from the Arabic article when there is one;
- a ☆ that adds it to your review deck.

**A sentence or a paragraph** is translated, and **Replace** puts the translation where the text was.

**A price** ("$49.99", "€15") shows in your own currency at the day's rate.

**The switch** at the top of the card, العربية ⇄ English, turns it into an English–English dictionary. The same works for French, German, Spanish, Turkish, Urdu and Persian words, explained in their own language.

**Pin** the card 📌 and it stays put while you look up more words.

## ✍️ The writing tools

Your personal English editor, explaining itself in Arabic:

| Tool | What it does |
|---|---|
| **تدقيق لغوي · Proofread** | Fixes grammar, spelling and word choice with as few changes as possible. Each mistake is struck through, its fix shown, and why, **in Arabic** |
| **تحسين · رسمي · ودّي · أقصر · أطول** | Improve, formal, friendly, shorter, longer |
| **اكتبه بالإنجليزية** | Turns an Arabic draft into natural English, not a word-for-word translation |
| **تلخيص · اشرح بالعربية · اكتب ردًّا** | Summarize, explain the idioms and slang in Arabic, draft a reply |

**Replace** puts the result back. With nothing selected, **Write new** writes an email or a message from a few words in Arabic, and **Insert** puts it at your cursor.

Every mistake goes into your **mistake journal**: your most frequent ones (articles? prepositions?) with a short rule and examples from your own writing.

## 🤖 Your AI, your choice

The writing tools and AI translation use the provider you pick:

| | Cost | Privacy |
|---|---|---|
| **Ollama** | Free | Your text never leaves your PC |
| **Gemini** | Free tier | Google may use free-tier requests to improve its products |
| **Claude** · **OpenAI** | Pay per use, your own key | Sent to the provider with your key |
| **A custom server** | Depends | Anything that speaks OpenAI's API: LM Studio, OpenRouter, Groq… |

Translation itself is **Google, the AI, or automatic** (Google first, the AI when Google can't). With Ollama, sentences translate with no internet at all. Keys stay in Windows' Credential Manager.

## 🪟 Lamha's window

At home next to Windows' own apps: Mica, Windows' controls, Arabic or English, light or dark.

| Page | |
|---|---|
| **Translate** | A box for anything, and **Today**: your daily goal, streak and word of the day |
| **Write** | A draft with all the writing tools, and the mistake journal |
| **Review** | Flashcards of your words, in the sentence you found them in: <kbd>Space</kbd>, then <kbd>1</kbd> <kbd>2</kbd> <kbd>3</kbd> |
| **History** | Every lookup by day, searchable, with a bookmark |
| **Clipboard** | Your copies: search, pin, translate, proofread, look up |
| **Settings** | Eleven categories, and a search in the title bar that finds settings, words and copies |

## 📚 Works offline

- **A dictionary built in:** 86,609 English entries, 20,620 Arabic words, inflections included (*went → go*, *mice → mouse*), from WordNet, Arabic WordNet, Wiktionary and CMUdict.
- **Language packs:** French, German, Spanish and Turkish dictionaries, a few MB each, from Settings → Without internet.
- **Voices:** Google's when you're online, Windows' own Arabic and English voices when you're not.

## 🔒 Privacy

In Lamha's own words (Settings → Privacy & history):

> Sent to Google's translation service: the text you select, and the sentence around it (if “Understand words from their sentence” is on). The word is sent to Wikipedia for the summary. In “Offline only” mode, words are looked up without sending anything. For a price in your currency, the day's exchange rates are fetched, without anything you selected. With the writing tools, the text goes to the provider you chose: Gemini, Claude or OpenAI with your key, or the server you set up; with Ollama or a server on your PC it stays on your computer. Clipboard history stays on this PC. No other data is collected.

Clipboard history is **off until you turn it on**, encrypted on disk, and never records password managers or card numbers. Lamha's log keeps events, never your text.

## 🧩 How it's built

Lamha 3 is a native Windows 11 app: **WinUI 3, C# / .NET 10, compiled ahead of time (NativeAOT)**, installed and updated with Velopack from GitHub Releases. No browser engine, no Node, nothing running in the background but a tray icon.

```
  Lamha.App       WinUI 3: the card, the windows, the design system
     │      ╲
     ▼       ▼
  Lamha.Windows   Lamha.Data
  Win32: shortcuts,    SQLite: your data,
  selection, clipboard, the dictionary,
  UI Automation        the language packs
           ╲     ╱
            ▼   ▼
         Lamha.Core    the brain: lookup, translation, AI, review…
                       with no idea it runs on Windows
```

**Light by design.** Measured like Task Manager, against budgets the build is checked on:

| | Memory |
|---|---|
| Waiting in the tray | **~1 MB** in Task Manager (52 MB committed) |
| The card open | ~42 MB |
| The main window open | ~40 MB |

The installer is about 52 MB, and Lamha updates itself.

**One design system.** Every screen uses Windows' own sizes, measured side by side with Windows Settings, plus Lamha's colours and its own pane icons. A test fails if a screen types a colour, a font size or a gap off the 4 px grid ([docs/design/system.md](docs/design/system.md)).

**Decisions on paper.** Every choice that shapes the app has a one-page record of what was chosen and why ([docs/decisions/](docs/decisions/)).

## 🚀 What's next

**Lamha 3.1** is on `main`: the new design system, a **mini translator** that floats over what you read, **side by side** translation of paragraphs, **paste and translate**, and a pinnable card.

After that, in [the roadmap](docs/roadmap.md):
- 📖 **Offline Wikipedia** and a reader, with no internet
- 🧠 **FSRS** reviews (Anki's newer scheduler), **export to Anki**, and practice built from your own mistakes
- 🌍 **Translation packs** that translate sentences with no internet and no AI
- …and ideas waiting for a decision

**Want to add something?** The roadmap's [Add-ons](docs/roadmap.md#add-ons-where-new-things-plug-in) part shows where a new AI provider, translation source, language pack, clipboard tool, Settings category or page plugs in.

## 🛠️ For developers

You need Windows 11, the .NET 10 SDK, and Visual Studio 2022 or later with *Desktop development with C++* (for NativeAOT).

```powershell
dotnet run --project tools/DictionaryBuilder -- dict artifacts/dict/lamha-dict.db   # the dictionary, once
dotnet build Lamha.slnx
dotnet test --solution Lamha.slnx
dotnet run --project src/Lamha.App -- --profile=C:\temp\lamha-test   # try it without touching your data
dotnet run --project src/Lamha.App -- --gallery                      # every component, live
```

| Folder | What's in it |
|---|---|
| `src/Lamha.Core` | The brain, with no Windows dependency |
| `src/Lamha.Data` | SQLite: your data, the dictionary, the language packs |
| `src/Lamha.Windows` | Win32: shortcuts, the selection, the clipboard, the tray, voices |
| `src/Lamha.App` | The WinUI 3 app and its design system (`Themes/`) |
| `tests/` | One test project per `src` project, running the real brain on in-memory SQLite |
| `tools/` | The dictionary builder, the strings tool, the memory check |
| `dict/` | The offline dictionary's data |
| `site/` | The download page |
| `docs/` | [The plan, decisions, design and roadmap](docs/README.md) |

The rules the code follows (layers, NativeAOT, strings in both languages, privacy, memory) are in [CLAUDE.md](CLAUDE.md). Branches: `main` is the app; each piece of work gets a short `lamha3/<what>` branch, merged when it's done.

## License

The code is **MIT** ([LICENSE](LICENSE)). The dictionary data is **CC BY-SA 4.0**, from WordNet, Arabic WordNet, Wiktionary and CMUdict ([dict/LICENSES.md](dict/LICENSES.md)).
