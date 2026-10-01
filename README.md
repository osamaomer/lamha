# لمحة · Lamha

**Select a word. Get it at a glance.**

Lamha is an Arabic-first dictionary, translator and English writing coach, inspired by the **Look Up** feature on iPhone and Mac. Select any English word and a small card shows what it means, in Arabic, in *this* sentence. Select a paragraph and it's translated. Select your own clumsy email and Lamha polishes it and explains, in Arabic, what you got wrong.

It comes in two versions that share one brain:

| | 🦊 **Firefox extension** | 🪟 **Windows app** |
|---|---|---|
| Works in | Web pages | Any program: WhatsApp, Word, Outlook, Teams… |
| How you start it | Select text → tap the button | Select text → press a shortcut |
| Page translation | ✅ | — |
| Clipboard history | — | ✅ |
| Lookups, writing tools, word review | ✅ | ✅ |

---

**Contents**

1. [✨ What Lamha does](#-what-lamha-does): features both versions share
2. [🦊 The Firefox extension](#-the-firefox-extension)
3. [🪟 The Windows app](#-the-windows-app)
4. [🛠️ For developers](#️-for-developers)
5. [🔒 Privacy and data sources](#-privacy-and-data-sources)

---

## ✨ What Lamha does

Everything in this part works the same in the extension and the Windows app.

### 🔎 Look up a word, translate a sentence

| Select a word or short phrase | Select a sentence or paragraph |
|---|---|
| The word, its pronunciation and a 🔊 button | The original text (tap to expand) |
| **Its meaning in this sentence**: Lamha reads the sentence around it | The full Arabic translation |
| Other meanings by part of speech (اسم، فعل، صفة…) | Listen and copy buttons |
| Definitions **translated to Arabic**, with the English original and an example | |
| Synonyms: tap one to look it up, and ← takes you back | |
| Spelling help: "هل تقصد: receive؟" | |
| A Wikipedia summary, from the Arabic article when there is one | |

- **📘 English–English dictionary:** a switch on every English word card, **العربية ⇄ English**, turns it into a monolingual dictionary: the pronunciation, the definition that fits your sentence (with its part of speech: *I mentioned* is the verb, *a mention* the noun), then every sense with examples and synonyms. Online, it agrees with the Arabic view: the sense whose Arabic matches Google's translation of your sentence leads. It works offline with the built-in dictionary and falls back to Google's English definitions for words it lacks. Lamha remembers your choice (also in Settings → القاموس). Review cards made this way show the English definition first and the Arabic meaning under it. With English as the translation language, words always use it.
- **🗣️ Other languages explained in their own language:** the same idea for Arabic, French, Turkish, Urdu, Persian, Spanish and German. The card's switch on any foreign word (**العربية ⇄ الفرنسية**, or **English ⇄ العربية** on an Arabic word) explains it in its own language: definitions, an example, synonyms, and for Arabic its root and plural, with the translation under it. Lamha remembers the choice for each language (also in Settings → القاموس → شرح الكلمات بلغتها). A word in your translation language is always explained (French with French selected: no more "maison → maison"). The explanation comes from Google's definitions when it has them, otherwise from the AI (Settings → الترجمة), which also works offline with Ollama. Offline explanations without an AI are English only for now.
- **Arabic → English too:** select Arabic text and it's translated to English. This can be turned off.
- **Other languages:** Settings → لغة الترجمة. Arabic is the default; English, French, Turkish, Urdu, Persian, Spanish and German are there too.

### 🌐 Google or AI: you choose who translates

Settings → الترجمة decides who translates sentences, paragraphs and the meaning of a word in its sentence:

| Choice | What happens |
|---|---|
| **تلقائي · Automatic** (default) | Google, fast and free. When there's no internet or Google refuses, the AI takes over. |
| **Google فقط · Google only** | Google, and nothing else. |
| **الذكاء الاصطناعي · AI** | The AI first: more natural with idioms, slang and tone, but slower. Google steps in if the AI fails. |

- **✨ ترجمة أدق · Better translation:** a link under Google's translation asks the AI for a second opinion. Google turns *"it's a piece of cake"* into "a piece of the cake"; the AI says «الأمر في غاية السهولة».
- **Its own translator:** translation can use a different AI from the writing tools, for example Gemini for writing and Ollama for translating. Each provider translates with its quickest model unless you pick another: Gemini Flash-Lite, Claude Haiku, or your Ollama model.
- **Offline:** with **Ollama** as the translator, sentences translate with no internet at all, even with the dictionary on *Local only*.
- **Pages:** whole-page translation stays on Google unless you allow the AI (a page is a lot of text for a free quota or a small local model).
- **A safety net:** AI models sometimes slip letters from another alphabet into a translation (we saw Korean and Hebrew in Arabic). Lamha checks every AI answer, asks again, and uses Google instead if it still looks wrong.
- Words keep the offline dictionary and Google's dictionary data first: for a single word, those beat a translation.

### 📚 An offline dictionary built in

About **86,000 English words** and **20,000 Arabic words** ship with Lamha, so word lookups don't need the internet:

- Arabic meanings grouped by sense: *bank* → مَصْرِف (the institution) · ضِفَّة (the side of a river).
- Definitions, examples, synonyms and IPA pronunciation.
- It knows inflected forms: *went → go*, *mice → mouse*, *studied → study*.
- Choose **Local first** (the default), **Local only** (never goes online) or **Online first** in Settings.
- **📥 Dictionaries to download:** French–French, German–German, Spanish–Spanish and Turkish–Turkish, from Settings → القاموس → قواميس للتنزيل (about 2–6 MB each, 34,000–40,000 common words). Once downloaded they stay on your device, and words in that language are explained in it with no internet and no AI: definitions, examples, synonyms, pronunciation, and forms such as *ging → gehen*, *evler → ev*, *ciudades → ciudad*. They come from each language's own Wiktionary. There is none for Arabic, Persian or Urdu (no open data good enough); the AI explains those.

### ✍️ Writing tools

Your personal English editor. Pick an AI provider in Settings → أدوات الكتابة (see [choosing a provider](#-choosing-an-ai-provider)), select some text, and choose a tool:

| Tool | What it does |
|---|---|
| تدقيق لغوي · Proofread | Fixes grammar, spelling and word choice with as few changes as possible. Each mistake is struck through, its fix appears, and it's explained **in Arabic**. |
| تحسين · رسمي · ودّي · أقصر · أطول | Improve, make formal, make friendly, make shorter, make longer (develops each point with the context it needs; details only you know become [placeholders]) |
| اكتبه بالإنجليزية | Turns an Arabic draft into natural English, not a word-for-word translation |
| تلخيص | Bullet-point summary, in Arabic or English |
| اشرح بالعربية | Explains the text, its idioms and its slang in Arabic |
| اكتب ردًّا | Drafts an English reply to a message. Tell it what you want to say, in Arabic if you like. |

Lamha offers the tools that fit the text: English text gets the editing tools, and Arabic text gets *write it in English*, *reply* and *summarize*.

- **Tool numbers:** each tool on the card has a number. Select text, press <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd>, then <kbd>1</kbd> to proofread, <kbd>2</kbd> to improve, and so on. Arabic digits work too.
- **استبدال (Replace)** puts the result back where the text was. **نسخ** copies it.
- **Write new:** with nothing selected, <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> opens *كتابة جديدة*. Describe what you want in Arabic or English, choose **Email** or **Message** and a tone (automatic, friendly, formal, **short** or **longer**), and Lamha writes it in English. **إدراج (Insert)** puts it at your cursor.
- **Double-click an empty box:** on a web page, double-click an empty text box or editor and the **كتابة** button appears. It opens *Write new* for that box: as an **Email** in Gmail, Outlook and other webmail, as a **Message** elsewhere. Typing hides it, and it never shows in password boxes. Turn it off in Settings → زر الكتابة بنقرتين على مربع فارغ. (Firefox only: in the Windows app, <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> with nothing selected does the same.)
- **Mistake journal** (Settings → سجل أخطائي): proofreading notes the *type* of each mistake (articles, prepositions, verb tenses…). Settings shows your most frequent types, with a short rule and examples from your own writing. Your top 3 are passed to the AI so it explains those especially clearly. The journal stays on your device and can be turned off.

#### 🤖 Choosing an AI provider

| | Cost | Privacy | Setup |
|---|---|---|---|
| **Ollama** | Free | Your text never leaves your PC | Install Ollama, then `ollama pull qwen3.5:4b` and pick the model in Settings |
| **Gemini** | Free tier | ⚠️ On the free tier Google may use what you send to improve its products: fine for practice, not for private or work text | A free key from <https://aistudio.google.com/apikey>, no card needed |
| **Claude** | Pay per use | Sent to Anthropic with your own key | A key from <https://console.anthropic.com/settings/keys> (billed separately from a Claude.ai subscription) |

- **Gemini models:** `gemini-3.8-flash` (the default, most accurate) or `gemini-3.5-flash-lite` (faster, with more requests allowed). The free tier has per-minute and daily limits; AI Studio shows yours. It runs on any PC, even one without a strong graphics card.
- **Claude models:** Opus 5 (default, most accurate), Sonnet 5, or Haiku 4.5 (fastest and cheapest).
- **Ollama in Firefox** needs one extra step. See [the extension's setup](#setting-up-ollama-for-the-extension).
- Keys are stored only on your device and never synced.

### 🗂️ Word review (flashcards)

Every English word you look up becomes a review card, with the sentence you found it in, its meaning in that sentence and a short definition. Words already in your history are imported the first time.

- **Where:** the **مراجعة** (Review) tab. A badge shows how many cards are waiting.
- **The front** shows the word, a 🔊 button, and your sentence with the word highlighted.
- **Answering:** <kbd>Space</kbd> shows the meaning. Then press **نسيت / صعبة / عرفتها** (forgot / hard / knew it), or <kbd>1</kbd> <kbd>2</kbd> <kbd>3</kbd>.
- **Scheduling** is a simplified SM-2, the idea behind Anki:
  - *Forgot* brings the card back in 10 minutes.
  - *Knew it* waits 2 days, then 5 days, then about 2.5× longer each time.
  - A word counts as learned once its gap reaches 3 weeks.
- **Adding and removing:** the 🔖 button on a lookup card adds or removes a word.
- **While you review:** a thin bar shows how far through the waiting cards you are. At the end, a summary says what you did (*reviewed 12 · knew 9 · hard 2 · forgot 1*).
- **Settings:** turn automatic adding on or off, choose 5–30 new words a day, set the daily goal, or delete all cards.
- **A copy of your data** (Settings → الخصوصية والسجل → *نسخة من بياناتي*): your cards and progress, word history and mistake journal in one file. *استرجاع* (Restore) adds a copy to what's already there, on the same device or another: nothing is deleted, a word in both keeps the schedule of the copy you reviewed last, and a word you deleted after saving the copy stays deleted.

### 🎯 Today: a daily goal, a streak and a word of the day

The **ترجمة** tab opens on a **Today** card (it steps aside while you translate something):
- **A daily goal ring:** words you look up (new ones only; the same word twice counts once) plus review answers, towards 10 a day by default. Settings → مراجعة الكلمات → **هدف اليوم** sets 5–30, or no goal. Reaching it gets a small celebration on the card or in Review.
- **A streak** (🔥) of days in a row with some practice. A day you haven't started yet doesn't break it.
- **A word of the day**, the same all day: a word of yours that's due for review (to refresh it before you forget it), otherwise a new, useful word from the offline dictionary with its Arabic meaning, definition and an example. **اعرض معناها** looks it up.

### 🎨 Language, looks and motion

- **Arabic or English interface:** Settings → المظهر → لغة الواجهة. *Automatic* uses Arabic on an Arabic system and English otherwise. In English the layout runs left to right, and proofreading explanations, mistake tips and summaries are in English too. Translations still go to your chosen translation language. Existing users keep Arabic when they update.
- **Light and dark themes** that follow the system, plus keyboard focus styles.
- **Animations you can switch:** Settings → المظهر → الحركة.
  - **Full:** the card grows out of the word you picked. Proofreading strikes each mistake before showing its fix, and AI answers appear word by word. Review cards flip and fly off the way you graded them. There are small celebrations too: a finished deck, a review streak, your 100th word, today's goal. 🎉 Copy buttons turn into a ✓, the bookmark pops when a word goes into review, and the 🔊 button's sound waves pulse while it speaks.
  - **Subtle:** short, calm fades and slides.
  - **Off:** nothing moves.
  - **Automatic** (default): Full, but Subtle on a slower PC and Off when Windows' *Animation effects* is off.
  - Animations never make you wait for a result, and keys work during them. A preview in Settings shows each level.

---

## 🦊 The Firefox extension

*Needs Firefox 140 or later.*

### How it feels

Select text on any page and a small **بحث** (Look Up) / **ترجمة** (Translate) button appears above it. Tap it and the card opens. With a writing provider set up, a **✨ كتابة** button appears beside it, including inside text boxes and editors such as email or chat.

**Three trigger modes** (Settings): the floating button (default), instant (the card opens as soon as you select), or <kbd>Alt</kbd>+select only.

### Extension-only features

- **🌍 Whole-page translation** (toolbar popup, right-click menu, or <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd>)
  - Translates as you scroll and follows content that loads later.
  - Translates whole sentences even when they're split across links or bold text, so the meaning survives.
  - Keeps the page working: links and site scripts are untouched.
  - A floating bar switches **العربية ⇄ الأصل** (Arabic ⇄ Original) instantly.
  - If Google is busy, Lamha quietly tries again. If some parts still fail, the bar says so and offers a retry button.
- **Right-click menu:** "لمحة: ترجمة «…»", *أدوات الكتابة* and *تلخيص الصفحة* (summarize the article).
- **Whole text box:** with the caret in a text box and nothing selected, <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> works on all of its text. Replace keeps the editor's undo working.
- **Toolbar popup** with:
  - **ترجمة:** a quick-translate box and your recent lookups.
  - **كتابة ✨:** write or paste a message, proofread it, improve it or turn Arabic into English, then copy the result. The draft is kept if the popup closes.
  - **مراجعة:** word review.
  - An on/off switch for the current site, and page translation.
- Works inside iframes. The card lives in a closed Shadow DOM, so websites can't break its styling.
- **With Lamha for Windows on the same PC:** Settings → *لمحة لـ Windows* → *اتصال* links the two (Firefox asks for permission once). The extension then uses the Wikipedia you downloaded in the app: the card's Wikipedia part works without internet, and *اقرأ المقالة في لمحة* opens the article in the app's reader (starting the app in the tray if it isn't running). **Your review cards are shared too:** words you add, review or delete in either one reach the other while the app is running (the app is never started just for this). Nothing goes over the network: Firefox starts a small helper the app wrote, which talks to the app on this PC only.

### ⌨️ Shortcuts

| Keys | What they do |
|---|---|
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd> | Look up / translate the selection |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> | Writing tools (then <kbd>1</kbd>–<kbd>9</kbd> to run one), or *Write new* with nothing selected |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd> | Translate the whole page (again to stop) |
| <kbd>Alt</kbd> + select | Open the card directly |
| <kbd>Esc</kbd> | Close the card |

Change them in Settings → تخصيص الاختصارات, or `about:addons` → ⚙ → Manage Extension Shortcuts.

### Installing

**To try it (until Firefox restarts):**

1. Open `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on…** and choose `manifest.json` in this folder.
3. The welcome page opens. Select the word *serendipity* on it. ✨

Or, with Node installed: `npx web-ext run` launches a clean Firefox with Lamha loaded.

**To keep it:** release versions of Firefox only install **signed** extensions.

1. Run `npx web-ext build`. It makes a `.zip` in `web-ext-artifacts/`. [web-ext-config.mjs](web-ext-config.mjs) leaves out the desktop app, tools and docs.
2. Upload it at <https://addons.mozilla.org/developers/>. Choose **On your own** (unlisted) for a private signed `.xpi`, or list it publicly.
3. Or use Firefox Developer Edition / Nightly with `xpinstall.signatures.required` set to `false` in `about:config`.

### Setting up Ollama for the extension

Firefox extensions need Ollama's permission to connect:

```bash
setx OLLAMA_ORIGINS "moz-extension://*"
```

Then restart Ollama. (The Windows app doesn't need this step.)

### 🎛️ Customising

- **Colours and fonts:** the tokens at the top of `content/styles.js` and `shared/ui.css`.
- **Translation service:** see the note in [Privacy](#-privacy-and-data-sources).

---

## 🪟 The Windows app

`desktop/` is Lamha as a standalone Windows app built with Electron. It runs the extension's own `background.js`, `local-dict.js` and `shared/` files unchanged, so the lookups, writing tools and review behave exactly the same. The main window has four tabs: **ترجمة · كتابة · مراجعة · الحافظة**. The Today card and up to 12 recent lookups fill the ترجمة tab.

### 🌐 Works in any program

Select text in WhatsApp, Word, Outlook, Teams or anything else, then:

| Keys | What they do |
|---|---|
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd> | The lookup / translation card, next to the mouse |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> | Writing tools (a number key runs one). With nothing selected: *Write new*, and Insert pastes where your cursor is |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> | Clipboard history panel |

**استبدال (Replace)** pastes the result straight back into that program.

<details>
<summary>How does it read another program's selection?</summary>

Lamha presses Ctrl+C for you to read the selection and Ctrl+V to paste the result, then puts your clipboard back the way it was, formatting included. The Windows calls go through [koffi](https://koffi.dev), so nothing needs compiling.

- Windows' own clipboard history (<kbd>Win</kbd>+<kbd>V</kbd>) may briefly show these copies.
- In terminals Lamha doesn't press Ctrl+C, because there it would stop the running program.
- With *فهم الكلمة من سياق الجملة* on, Lamha also reads the sentence around a word through Windows UI Automation, so it can pick the right meaning.
</details>

### 📖 Wikipedia without internet

Settings → **ويكيبيديا بدون إنترنت** downloads Wikipedia to your PC, so the card's Wikipedia part keeps working with no connection. It's optional: nothing is downloaded until you choose something.

- **What you can download** comes from [Kiwix](https://kiwix.org)'s catalog, in the language you pick: *Top articles* or *All articles*, each as *Mini* (introductions only, all the card needs), *Full, no pictures* or *Full, with pictures*, plus topic sets (medicine, history…). For example, Arabic Top articles · Mini is 226 MB for 231,000 articles; all of Arabic Wikipedia with pictures is 19 GB.
- **Downloads** come from the fastest of Kiwix's mirrors. They can be paused, resume where they stopped (also after a restart or a dropped connection, from another mirror), and are checked against Kiwix's SHA-256 before they're used. Files over 2 GB ask first.
- **Where:** `%APPDATA%\Lamha\wikipedia` by default. *تغيير* picks another folder (a big file may belong on another drive).
- **A file you already have** (from Kiwix, for example) can be added as it is: Lamha reads it where it is, and removing it from Lamha never deletes it.
- **When the card uses it:** when Wikipedia can't be reached, or always with *استخدم النسخة المنزّلة أولًا* (faster, and nothing goes online). With the dictionary on *Local only*, only the downloaded copy is used. The card marks it *من النسخة المنزّلة* with the file's month.
- With no internet, nothing links an English word to its Arabic article, so the card looks for the article under the word's translation (Paris → باريس), then in an English file if you have one.
- **Read whole articles in Lamha:** *اقرأ المقالة في لمحة* on the card, *قراءة* next to a file in Settings, or **ويكيبيديا** in the tray menu opens Lamha's own reader. It has a search box that suggests titles as you type, back and forward (also <kbd>Alt</kbd>+arrows and the mouse's side buttons), contents, text size, a random article, and *open on Wikipedia* when you're online. Select any word in an article to look it up with Lamha, as on a web page. Mini files hold introductions only, so their articles end after the lead.
- **From Firefox too:** the Lamha extension can use these files (Settings → Firefox in the app, *لمحة لـ Windows* in the extension; see the Firefox part).
- **Safe by design:** an article is rebuilt from a short list of allowed tags before it's shown, so nothing in a downloaded file can run (no scripts, event handlers, styles, frames or forms). Links are handled by the reader, never followed by the page. Pictures come only from the file, and a strict content policy blocks everything else.

### 📋 الحافظة: clipboard history

A history of what you copy in any program, with Lamha's language tools on every item. Search understands Arabic: it ignores diacritics and letter variants, so `مصرف` finds `مَصْرِف` and `احمد` finds `أحمد`.

- **Off until you turn it on:** Settings → الحافظة, or the card <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> shows the first time.
- **Pinned items stay at the top** of the list and of search results. The quick panel still opens on your newest copy, so <kbd>Enter</kbd> pastes what you just copied.
- **The quick panel** (<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd>) opens at the mouse:

  | Keys | Action |
  |---|---|
  | type | Search |
  | <kbd>Enter</kbd> / <kbd>Shift</kbd>+<kbd>Enter</kbd> | Paste into the program you were in / paste as plain text |
  | <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Paste one of the first nine |
  | <kbd>Ctrl</kbd>+<kbd>P</kbd> | Pin |
  | <kbd>Delete</kbd> | Remove (with undo) |
  | <kbd>Tab</kbd> | Lamha's tools for the item, numbered: translate, write in English, proofread, summarize, look up, add to review. <kbd>1</kbd>–<kbd>9</kbd> runs one. You always see the result before pasting it; <kbd>Esc</kbd> goes back a step. |

- **The الحافظة tab** in the main window lists everything, with the full text, labels, pins and the same tools. **مسح غير المثبّت** empties the history but keeps pinned items.
- **Private by design:**
  - Saved in `%APPDATA%\Lamha\clipboard.json`, encrypted with Windows (DPAPI) for your user account.
  - Nothing is sent anywhere unless you press a tool on an item.
  - Copies from password managers (KeePass, KeePassXC, 1Password, Bitwarden, Enpass, Dashlane, NordPass) are never recorded. Add more programs in Settings, or with **تجاهل هذا البرنامج** on an item. Lamha also honours the signals password managers put on the clipboard for Windows' own history.
  - Bank-card numbers are skipped (on by default).
- **Also in Settings:** how many items to keep (500 by default) and automatic deletion of unpinned items (after 30 days by default).
- **Pause** from the tray: 15 minutes, 1 hour, or until you resume.
- Windows' own history (<kbd>Win</kbd>+<kbd>V</kbd>) is separate and may still record your copies. Turn it off in Windows Settings → System → Clipboard if you only want Lamha's.

### 🧰 Living in the tray

- Closing the window keeps Lamha in the tray, next to the clock. The tray menu opens the app, the clipboard panel, Review, Writing or Settings. It can also pause the clipboard, switch animations, **Start with Windows**, and quit.
- **Review reminders:** a Windows notification when cards are waiting, at most every 4 hours.
- **Ollama** works without the `OLLAMA_ORIGINS` step.
- **Your data** (settings, cards, the mistake journal) is saved in `%APPDATA%\Lamha`. API keys are encrypted with DPAPI.
- **Less memory:** Lamha draws without the graphics card, which saves about 25 MB. For smoother animations, turn on Settings → المظهر → الرسم بمعالج الرسوميات. The change applies when Lamha starts again (Settings offers to restart).
- **Updates:** the installed app checks [GitHub Releases](https://github.com/osamaomer/lamha/releases) at startup and every 6 hours, downloads new versions in the background and installs them when Lamha restarts. The tray has **التحقق من التحديثات**, and Settings → التحديثات can turn automatic updates off. The Portable version can't replace itself: it tells you a new version is out and links to the download.

### Installing

Download **Lamha-Setup-&lt;version&gt;.exe** from [Releases](https://github.com/osamaomer/lamha/releases). Only the Setup version updates itself; the Portable one doesn't.

The app isn't code-signed, so the first time it runs Windows SmartScreen says "Windows protected your PC". Choose *More info → Run anyway*.

### Building from source

```bash
cd desktop
npm install
npm start          # run from source
npm run smoke      # self-test: starts with a temporary profile, checks everything, quits
npm run shots      # a picture of every screen (light/dark × Arabic/English) in %TEMP%\lamha-shots; with Ollama running, the writing tools' results too
npm run memory     # the app's memory as Task Manager sees it, with a temporary profile (-- -Use, -Window, -Gpu)
npm run dist       # builds dist/Lamha-Setup-<version>.exe and dist/Lamha-Portable-<version>.exe
```

### 🚀 Releasing a new version

GitHub Actions builds releases ([release.yml](.github/workflows/release.yml)), so no token or build tools are needed on your PC.

1. Run the full self-test on Windows: `cd desktop && npm run smoke`. The cloud build runs the unit tests and the lint, but not this one, because it needs a real desktop.
2. Write what's new at the top of [`shared/changelog.js`](shared/changelog.js), in Arabic and English. The app shows it in Settings → التحديثات, and it becomes the release notes. A release without an entry stops (and `npm test` fails).
3. Raise the version, commit, and push a matching tag from the repository folder:
   ```bash
   npm version 1.8.1 --no-git-tag-version --prefix desktop   # updates desktop/package.json and its lockfile
   git commit -am "Lamha 1.8.1"
   git tag v1.8.1
   git push && git push origin v1.8.1
   ```
   Keep `manifest.json`'s `version` in step with it.
4. The **Release** workflow checks the tag matches the version, runs the tests, builds both installers, and publishes them with `latest.yml` as a GitHub Release, with the changelog entry as its notes. Follow it in the **Actions** tab.
5. Installed copies pick it up on their next check.

The language packs have their own workflow ([packs.yml](.github/workflows/packs.yml)): pushing a `packs-v*` tag builds them and publishes them as a release that is never marked "latest" (the app's updates come from the latest release).

---

## 🛠️ For developers

> Starting a new session with Claude Code? [CLAUDE.md](CLAUDE.md) has the architecture, the conventions and where the work stands. Claude Code reads it automatically.

### Project layout

```
manifest.json               MV3 manifest (Firefox 140+)
background.js               The brain: network calls, caching, settings, history, AI tools, flashcards, menu, shortcuts
local-dict.js               The offline dictionary (reads dict/)
packs.js                    Language packs to download (French–French…): download, keep, look up
content/
  content.js                Selection, floating button, lookup and writing card, page-translation bar
  page-translator.js        Whole-page translation engine
  styles.js                 CSS for the card (injected into a closed shadow root)
popup/                      Toolbar popup (the main window in the Windows app)
options/                    Settings + welcome page (i18n-options.js has its strings)
shared/
  i18n.js                   Interface language (Arabic / English) and the shared strings
  lamha-ai.js               Mistake categories, AI error messages, word diff
  motion.js, motion.css     The Animations setting and its helpers
  ui.css                    Design tokens for popup and options
  arabic-normalize.js       Arabic-aware search and language detection (clipboard history)
desktop/                    Windows app (Electron)
  main.js                   Main process: runs background.js with a browser.* stand-in, windows, tray, shortcuts
  preload.js, storage.js    The browser.* API for pages; storage in JSON files
  native.js, selection.js   Windows calls; reading and pasting selections in other apps
  pack-store.js             Where downloaded language packs are kept (files in %APPDATA%\Lamha\packs)
  native-bridge.js          The Firefox extension's link to the app: the native messaging host (PowerShell) and the pipe it talks to
  build/installer.nsh       Uninstalling removes Firefox's registry entry for the link
  zim.js                    Reads Wikipedia's offline files (.zim, Kiwix's format): articles, redirects, titles
  wiki-library.js           Offline Wikipedia: Kiwix's catalog, downloads (mirrors, resume, SHA-256), the card's summaries, the reader's articles
  uia-context.js            Starts the helper that reads the sentence around a word in other apps
  uia-helper.cs             That helper (lamha-uia.exe, UI Automation), built with Windows' own C# compiler: npm run helper
  app-rules.js              What each window is told and may ask, what Firefox may ask, where the floating windows go
  clipboard-*.js            Clipboard history: capture, store, privacy, tools
  updater.js                Updates from GitHub Releases
  renderer/                 Card window, clipboard UI, desktop strings and styles
  renderer/wiki/            The Wikipedia reader: reader.html/js/css, and sanitize.js (rebuilds articles from an allow-list)
  scripts/                  run, smoke test, screenshots, memory, icons, building the helper, copying the extension files in
dict/                       Offline dictionary data (generated): en/ and forms/ by a word's first two letters, ar/
tools/                      Tests (zim-fixture.mjs writes small .zim files for them), bench.mjs, and the dictionary builders
```

### Tests

```bash
npm install     # once: installs jsdom for the UI test
npm test        # all five test files
npm run test:quiet                      # the same, printing only failures and the totals
node tools/test-writing.mjs -q review   # one file, only the tests whose name contains "review" (every file but test-ui)
npm run lint    # web-ext lint (0 errors, 0 warnings expected)
npm run bench   # performance numbers: lookups on a simulated network (normal, dead, none), the dictionary, the deck,
                # and the Windows app's storage, clipboard search and downloaded Wikipedia (not a test: no pass or fail)
```

| File | What it covers |
|---|---|
| `tools/test-writing.mjs` | The writing tools and flashcard scheduling, running the real background code with a fake browser and a fake network (no key needed). Add `--ollama` for one real proofread through local Ollama, or `--gemini` with `GEMINI_API_KEY` set for both Gemini models. |
| `tools/test-ui.mjs` | Renders the real popup and settings pages in jsdom and clicks through review, compose and the journal; also the Windows app's Wikipedia reader (its article cleaner, search, links, back and forward) |
| `tools/test-clipboard.mjs` | Clipboard history: Arabic search, the encrypted store, tools on clips, privacy rules |
| `tools/test-updater.mjs` | The desktop updater |
| `tools/test-desktop.mjs` | The Windows app's own parts: offline Wikipedia (reading .zim files, the card's summary, Kiwix's catalog, downloads that break and resume), the helper that reads the sentence around a word in other apps, the language-pack store and the changelog. Set `LAMHA_TEST_ZIM` to a real `wikipedia_ar_*.zim` from Kiwix to check it too. |
| `tools/compare-translation.mjs` | Not a test: runs the same sentences through Google, Gemini and Ollama and writes `translation-report.md`, to judge AI translation quality. Needs `GEMINI_API_KEY` and/or `--ollama <url>`. |

### Building the language packs

`tools/build_packs.py` builds `dist-packs/<lang>.json.gz` (git-ignored) from the Wiktionary edition written in that language, as extracted by kaikki.org, keeping the most common words by wordfreq. It streams the source (Turkish 44 MB … French 730 MB), so nothing big is saved.

```bash
pip install wordfreq
python tools/build_packs.py tr          # also fr, de, es
```

To publish, push a `packs-v*` tag (see "Releasing a new version"): GitHub builds and publishes them. `RELEASE` in `packs.js` names the release (`packs-v1`); update `CATALOG` there (download size and word count, which Settings shows before downloading). A new format means a new `FORMAT` and a new release tag.

### Rebuilding the offline dictionary

`dict/` is generated by `tools/build_dict.py` from WordNet 3.0, Arabic WordNet, English Wiktionary (via kaikki.org) and CMUdict. See the script's docstring for details.

```bash
pip install nltk wordfreq
python -c "import nltk; [nltk.download(p) for p in ('wordnet','omw-1.4','cmudict')]"
curl -LO https://kaikki.org/dictionary/English/kaikki.org-dictionary-English.jsonl.gz
python tools/build_dict.py --kaikki kaikki.org-dictionary-English.jsonl.gz
```

---

## 🔒 Privacy and data sources

Lamha sends nothing until you ask it to.

| What | Where it goes | When |
|---|---|---|
| Translations, dictionary, pronunciation | Google Translate's public endpoints (`translate.googleapis.com`, failing over to `clients5.google.com` when one is rate-limited) | When you open a card or translate a page |
| Sentences, paragraphs and pages, with AI translation | Your chosen translator: Ollama (your PC), Gemini or Claude | When the translation service uses the AI (Settings → الترجمة), or you press ✨ Better translation |
| The sentence around a selected word (the word included) | The translation service, to pick the right meaning | With *فهم الكلمة من سياق الجملة* on (the default). On Local only, only a local AI (Ollama) gets it. |
| Encyclopedia summaries | Wikipedia's REST API, or the Wikipedia you downloaded (Windows app), which sends nothing | For word lookups, when enabled |
| Firefox ↔ the Windows app (Wikipedia, review cards) | Nowhere: a helper on this PC (PowerShell, written by the app) passes messages between Firefox and the app over a local pipe | Only after you press اتصال in the extension's Settings |
| A problem report (settings, counts, recent error codes; never your text, words, keys or addresses) | Nowhere by itself: *الإبلاغ عن مشكلة* shows it, copies it, or opens a GitHub issue page with it for you to read and submit | Only when you press one of those buttons |
| Offline Wikipedia (Windows app) | Kiwix: its catalog (`library.kiwix.org`), then the file from one of its mirrors (`download.kiwix.org`, `dumps.wikimedia.org`…) | Only when you open that Settings section, and when you press تنزيل |
| Language packs (French–French…) | Downloaded from this project's GitHub releases; nothing is sent | Only when you press تنزيل in Settings |
| Writing tools | The provider you chose: Ollama (your PC, `localhost:11434`), Gemini (`generativelanguage.googleapis.com`) or Claude (`api.anthropic.com`) | Only when you press a tool |
| Clipboard items (Windows app) | The same service the tool uses anywhere else | Only when you press a tool on an item |

- **Local first** looks single words up in the built-in dictionary. Nothing about the word is sent, unless the sentence context is on or the dictionary has definitions but no Arabic word (then one small request fetches the main meaning).
- **Local only** never goes online for words at all.
- Pointing Ollama at another computer over `http://` sends your text across the network unencrypted.
- Lookup history, review cards and the mistake journal stay on your device (review cards also on the Windows app on the same PC, once the two are linked). In Firefox, settings sync through your Firefox account; API keys never do.
- The Windows app's windows can't use the camera, microphone, location or notifications (only copying to the clipboard), the card can't ask the app for anything it doesn't do itself, and the app checks its own files when it starts: a modified copy doesn't run.

> **A note on Google Translate:** the `gtx` endpoints are free and need no key, but they're unofficial. For heavy or commercial use, switch `translateBatch()` and `lookup()` in `background.js` to an official API (Google Cloud Translation, DeepL, Azure). Nothing else needs to change.

---

## License

The code is under the [MIT License](LICENSE). The dictionary data in `dict/` is CC BY-SA 4.0; see [dict/LICENSES.md](dict/LICENSES.md). The downloadable language packs come from the French, German, Spanish and Turkish Wiktionaries (CC BY-SA 4.0), extracted by [kaikki.org](https://kaikki.org) (wiktextract), with word frequencies from wordfreq (CC BY-SA 4.0). Offline Wikipedia files are Wikipedia's articles (CC BY-SA 4.0), packaged by [Kiwix](https://kiwix.org) (openZIM); the app downloads them, they aren't part of this repository.
