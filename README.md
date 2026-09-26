# Lamha — لمحة

An Arabic-first Firefox extension for looking up English words and translating anything on the web, inspired by the **Look Up** feature in iOS and macOS.

Select text on any page and a small **بحث** (Look Up) / **ترجمة** (Translate) button appears above it. Tap it to open a card with:

| For a word or short phrase | For a sentence or paragraph |
|---|---|
| Headword with pronunciation (IPA-style) and 🔊 audio | Original text (tap to expand) |
| Main Arabic meaning and transliteration | Full Arabic translation |
| Other meanings grouped by part of speech (اسم، فعل، صفة…) | Listen / copy |
| Oxford-style definitions **translated to Arabic**, with the English original and an example | |
| Synonyms: tap one to look it up; a back button returns you | |
| Spelling suggestions ("هل تقصد: receive؟") | |
| Wikipedia summary (Arabic article when one exists) | |

### ✨ Writing tools (Ollama or Claude)

Choose a provider in Settings → أدوات الكتابة:

- **Ollama (free):** runs an open model such as `qwen3.5:4b` on your own computer. It costs nothing, works offline, and your text never leaves the PC. Setup:
  1. Install Ollama.
  2. Allow extensions to connect with `setx OLLAMA_ORIGINS "moz-extension://*"`, then restart Ollama.
  3. Download a model with `ollama pull qwen3.5:4b`, then pick it in Settings.
- **Gemini (free, online):** uses Google's Gemini API with a free key from <https://aistudio.google.com/apikey>, and no card is needed.
  - Models: `gemini-3.8-flash`, the default and most accurate, or `gemini-3.5-flash-lite`, faster with more requests allowed.
  - The free tier has per-minute and daily limits. Your current limits are shown in AI Studio.
  - ⚠️ On the free tier Google may use what you send to improve its products. Good for practice, not for private or work text.
  - Works on any PC, including ones without a strong graphics card.
- **Claude (most accurate):** uses your own Claude API key.

Once one is set up, a **✨ كتابة** button appears next to بحث/ترجمة, including inside text boxes and editors such as email or chat. Pick a tool:

| Tool | What it does |
|---|---|
| تدقيق لغوي — Proofread | Fixes grammar, spelling and word choice with minimal changes. It shows the changes inline and explains each mistake **in Arabic**. |
| تحسين الأسلوب / رسمي / ودّي / أقصر | Improve, make formal, make friendly, make shorter |
| اكتبه بالإنجليزية | Turns an Arabic draft into natural, fluent English (not a literal translation) |
| تلخيص | Bullet-point summary in Arabic or English |
| اشرح بالعربية | Explains the text, its idioms and slang in Arabic |
| اكتب ردًّا | Drafts an English reply to a message. You can say what you want to answer, in Arabic. |

- **استبدال (Replace)** writes the result back into the text box, and the editor's undo still works. **نسخ** copies it.
- With the caret in a text box and nothing selected, <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> (or right-click → *أدوات الكتابة*) works on the whole box.
- Right-click a page → *تلخيص الصفحة* summarizes the article.
- **Compose box**: the toolbar popup has a **كتابة ✨** tab. Write or paste a message there, then proofread it, improve it, or turn Arabic into natural English, then copy the result. The draft is kept if the popup closes.
- **Mistake journal** (Settings → سجل أخطائي):
  - Proofreading records the type of each mistake: articles, prepositions, verb tenses and so on.
  - Settings shows your most frequent types, with a short Arabic rule and examples from your own writing.
  - Your top 3 types are passed to the AI, so it explains those points especially clearly.
  - The journal is stored only on your device, and it can be turned off.
- The model can be changed in Settings: Opus 5 (default, most accurate), Sonnet 5, or Haiku 4.5 (fastest and cheapest).

The API key comes from <https://console.anthropic.com/settings/keys>. It is billed per use, separately from a Claude.ai subscription. The key is stored in `storage.local` on your device and is never synced.

### 🗂️ Word review (flashcards)

Every English word you look up becomes a review card. The card keeps the sentence you found the word in, its meaning in that sentence, and a short definition. Words already in your history are imported the first time.

- **Where:** open the toolbar popup → **مراجعة** (Review). The toolbar icon shows how many cards are waiting.
- **Front of the card:** the word, a 🔊 button, and your sentence with the word highlighted.
- **Answering:** <kbd>Space</kbd> shows the meaning. Then choose **نسيت / صعبة / عرفتها** (forgot / hard / knew it) with <kbd>1</kbd> <kbd>2</kbd> <kbd>3</kbd>.
- **Scheduling:** a simplified SM-2, the same idea as Anki.
  - *Forgot* brings the card back in 10 minutes.
  - *Knew it* waits 2 days, then 5 days, then roughly 2.5× longer each time.
  - A word counts as learned once its gap reaches 3 weeks.
- **Adding and removing:** the 🔖 button on a lookup card adds or removes a word.
- **Settings:** turn automatic adding on or off, choose how many new words per day (5–30), or delete all cards.

Other features:

- **Whole-page translation** (toolbar popup, right-click menu, or <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd>)
  - Translates lazily as you scroll and follows content that loads later.
  - Translates whole sentences even when they're split across links or bold text, so context is kept.
  - Keeps page elements intact, so links and site scripts still work.
  - A floating bar switches **العربية ⇄ الأصل** (Arabic ⇄ Original) instantly.
- **Offline dictionary built in**: about 86,000 English words and 20,000 Arabic words. Word lookups need no internet:
  - Arabic meanings grouped by sense, e.g. *bank* → مَصْرِف (institution) · ضِفَّة (edge of river).
  - Definitions, examples, synonyms and IPA pronunciation.
  - Inflected forms are understood: *went → go*, *mice → mouse*, *studied → study*.
  - Choose **Local first** (default), **Local only** (fully offline) or **Online first** in Settings.
- **Selected Arabic text is translated to English** (this can be turned off).
- **Keyboard**: <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd> looks up the selection and <kbd>Esc</kbd> closes the card. <kbd>Alt</kbd> + select opens the card directly.
- **Right-click menu**: "لمحة: ترجمة «…»" (translate the selection).
- **Toolbar popup** with:
  - A quick-translate box.
  - An on/off switch for this site.
  - Page translation.
  - Recent lookups.
- **Three trigger modes**: floating button (default), instant, or Alt+select only.
- Light and dark themes that follow the system, a right-to-left Arabic UI, reduced-motion support and keyboard focus styles.
- Works inside iframes. The UI lives in a closed Shadow DOM, so websites can't break its styling.

## Install

### For development (temporary, until Firefox restarts)
1. Open `about:debugging#/runtime/this-firefox` in Firefox.
2. Click **Load Temporary Add-on…** and choose `manifest.json` in this folder.
3. The welcome page opens. Select the word *serendipity* on it to try the extension.

Or, with Node installed:
```bash
npx web-ext run          # launches a clean Firefox with the extension loaded
```

### Permanent install
Release versions of Firefox only install **signed** extensions:
1. Build the package with `npx web-ext build`, which creates a `.zip` in `web-ext-artifacts/`. [web-ext-config.mjs](web-ext-config.mjs) leaves out `desktop/`, `tools/`, `store-assets/` and this README.
2. Upload it at <https://addons.mozilla.org/developers/>. Choose **"On your own"** (unlisted) for a private signed `.xpi`, or list it publicly on the store.
3. Alternatively, use Firefox Developer Edition or Nightly and set `xpinstall.signatures.required` to `false` in `about:config`.

Validate with `npx web-ext lint`. It currently reports 0 errors and 0 warnings.

Test the writing tools with `node tools/test-writing.mjs`. The test runs the background code with a fake browser and a fake network, so it needs no key. Add `--ollama` to also run one real proofread through your local Ollama. Add `--gemini`, with `GEMINI_API_KEY` set, for a real proofread through both Gemini models. The same file also tests the flashcard scheduling.

`node tools/test-ui.mjs` renders the real popup and settings pages in jsdom and clicks through the review, compose and journal screens. It needs jsdom, a development dependency at the repository root: run `npm install` once.

`node tools/test-clipboard.mjs` tests the desktop app's clipboard history: Arabic-aware search, the encrypted store, the tools on clips (against the real `background.js` with a fake network) and the privacy rules. `npm test` runs all three test files.

## Windows desktop app

`desktop/` contains Lamha as a standalone Windows app built with Electron. It runs the extension's own `background.js`, `local-dict.js` and `shared/` unchanged, on top of a small `browser.*` replacement. The popup becomes the main window: **ترجمة · كتابة ✨ · مراجعة · الحافظة**.

- **Works in any program:** select text in WhatsApp, Word, Outlook, Teams or any other app, then press a shortcut:
  - <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd> opens the lookup/translation card next to the mouse.
  - <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> opens the writing tools.
  - **استبدال (Replace)** pastes the result back into that program.
  - How it works: Lamha sends Ctrl+C to read your selection and Ctrl+V to paste the result, and restores your clipboard afterwards, including formatting. The Windows calls go through [koffi](https://koffi.dev), so nothing needs compiling.
  - Clipboard history (<kbd>Win</kbd>+<kbd>V</kbd>) may briefly show these copies.
  - In terminals Lamha doesn't send Ctrl+C, because there it would stop the running program.
- **Tray icon:** closing the window keeps Lamha in the tray, next to the clock. The tray menu opens the app, the clipboard history, the review tab, the writing tab or Settings, pauses the clipboard history, has a **Start with Windows** option, and quits.
- **Review reminders:** a Windows notification when cards are waiting, at most every 4 hours.
- **Ollama:** works without the `OLLAMA_ORIGINS` step, which is only needed for browser extensions.
- **Your data:** settings, cards and the mistake journal are saved in `%APPDATA%\Lamha`.

### الحافظة (Clipboard history)

A history of what you copy in any program, with Lamha's language tools on every item. Search understands Arabic: it ignores diacritics and letter variants, so `مصرف` finds `مَصْرِف` and `احمد` finds `أحمد`.

- **Off until you turn it on.** Enable it in Settings → الحافظة, or from the card that <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> shows the first time.
- <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> opens a small panel at the mouse. Type to search, then press <kbd>Enter</kbd> to paste into the program you were in, or <kbd>Shift</kbd>+<kbd>Enter</kbd> to paste as plain text. <kbd>Ctrl</kbd>+<kbd>P</kbd> pins an item, <kbd>Delete</kbd> removes it (with undo), and <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>9</kbd> pastes one of the first nine.
- <kbd>Tab</kbd> on an item opens Lamha's tools for it: translate, write in English, proofread, summarize (in Arabic or English), look up, or add a word to review. You always see the result before pasting it.
- The **الحافظة** tab in the main window lists everything, with the full text, labels, pins and the same tools.
- **Local and encrypted:** the history is saved in `%APPDATA%\Lamha\clipboard.json`, encrypted with Windows (DPAPI) for your user account. Nothing is sent anywhere unless you press one of the tools on an item.
- **Excluded programs:** copies from password managers (KeePass, KeePassXC, 1Password, Bitwarden, Enpass, Dashlane, NordPass) are never recorded. Add more in Settings → الحافظة, or with **تجاهل هذا البرنامج** on an item. Lamha also honours the signals password managers put on the clipboard for Windows' own history.
- **Also in Settings:** how many items to keep (500 by default), automatic deletion of unpinned items (after 30 days by default), skipping bank-card numbers (on by default), and clearing the history.
- **Pause** from the tray: 15 minutes, 1 hour, or until you resume.
- **Windows' own clipboard history** (<kbd>Win</kbd>+<kbd>V</kbd>) is separate and may still record your copies. Turn it off in Windows Settings → System → Clipboard if you only want Lamha's.

```bash
cd desktop
npm install
npm start          # run from source
npm run smoke      # self-test: starts the app with a temporary profile, checks everything, quits
npm run dist       # builds dist/Lamha-Setup-<version>.exe and dist/Lamha-Portable-<version>.exe
```

The app is not code-signed. The first time it runs, Windows SmartScreen shows "Windows protected your PC". Choose *More info → Run anyway*.

## Project layout

```
manifest.json            MV3 manifest (Firefox 140+)
background.js            All network calls, caching, settings, history, menu, shortcuts
content/styles.js        CSS for the in-page UI (injected into a closed shadow root)
content/content.js       Selection detection, floating button, lookup card, page-translation bar
content/page-translator.js  Whole-page translation engine
popup/                   Toolbar popup
options/                 Settings + welcome page
shared/ui.css            Design tokens shared by popup and options
shared/lamha-ai.js       Mistake categories, AI error messages, word diff (background, content, popup, options)
desktop/                 Windows app (Electron): main.js, preload.js, storage.js,
                         native.js + selection.js (any-app shortcuts),
                         clipboard-*.js (clipboard history), renderer/, scripts/
shared/arabic-normalize.js  Arabic-aware search normalization and language detection (desktop clipboard history)
icons/icon.svg
```

## Offline dictionary

The data in `dict/` is generated by `tools/build_dict.py` from WordNet 3.0, Arabic WordNet, English Wiktionary (via kaikki.org) and CMUdict. See the script's docstring for rebuild steps and [dict/LICENSES.md](dict/LICENSES.md) for attribution. The data is **CC BY-SA 4.0**.

Rebuild it with:
```bash
pip install nltk wordfreq
python -c "import nltk; [nltk.download(p) for p in ('wordnet','omw-1.4','cmudict')]"
curl -LO https://kaikki.org/dictionary/English/kaikki.org-dictionary-English.jsonl.gz
python tools/build_dict.py --kaikki kaikki.org-dictionary-English.jsonl.gz
```

## Data sources and privacy

- **Translation, dictionary, definitions, synonyms, pronunciation:** Google Translate's public web endpoints (`translate.googleapis.com`, with automatic failover to `clients5.google.com` when one is rate-limited). Nothing is sent until you select text and open the card, or start page translation.
- **Encyclopedia:** Wikipedia's REST API.
- **Writing tools:** either Ollama on your own computer (`localhost:11434` by default, where nothing leaves the PC), or the Claude API (`api.anthropic.com`) called with your own key. Text is sent only when you press a tool. The selected text is sent, or the page's text when you ask for a page summary.
- The history of looked-up words stays in local storage on your device. Settings sync through your Firefox account.
- **Clipboard history (Windows app):** copied text stays on your PC, encrypted. A clip is sent only when you press one of Lamha's tools on it (ترجم، دقّق، اكتبه بالإنجليزية…), to the same service that tool uses everywhere else.
- Sent to Google Translate: the text you select, the **sentence around a selected word** (used to pick the right meaning; turn off with *فهم الكلمة من سياق الجملة*), and page text when you choose to translate a page.
- In **Local first** and **Local only** modes, single words are looked up in the built-in dictionary without sending them anywhere (the context sentence is still sent in Local first unless disabled).

> **Note:** Google's `gtx` endpoints are free and need no API key, but they are unofficial. For heavy or commercial use, switch `translateBatch()` and `lookup()` in `background.js` to an official API such as Google Cloud Translation, DeepL or Azure. Everything else stays the same.

## Customising

- **Default target language:** Settings → لغة الترجمة (Translation language). Arabic is the default; English, French, Turkish, Urdu, Persian, Spanish and German are also available.
- **Keyboard shortcuts:** Settings → تخصيص الاختصارات (Customise shortcuts), or `about:addons` → ⚙ → Manage Extension Shortcuts.
- **Colours and fonts:** edit the tokens at the top of `content/styles.js` and `shared/ui.css`.

## License

The code is released under the [MIT License](LICENSE). The offline dictionary data in `dict/` is licensed under CC BY-SA 4.0; see [dict/LICENSES.md](dict/LICENSES.md).
