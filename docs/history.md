# Lamha: work history

What was built in each session, why, and what was found on the way, oldest first (from 1.8.1, 2026-09-27). Moved out of CLAUDE.md on 2026-10-01 to keep each session small: search it (`grep -n`) when a task needs the reasons behind earlier work. What each release did for users is in `shared/changelog.js`; open work is in CLAUDE.md → Open items.

## Done (2026-09-27, released in 1.8.1 with the translation service below)

- README rewritten: shared features first, then separate **Firefox extension** and **Windows app** parts, then developer notes and privacy.
- This file added (and kept out of the extension package in `web-ext-config.mjs`).
- Seven fixes from a code review, each with a test that fails on the old code:
  1. **Mistake journal sorted again** (`options.js` `renderJournal`: the `.sort()` had fallen into a comment in 1.6.1).
  2. **In-page Summarize follows the interface language** (`content.js` `pickTool`).
  3. **Arabic → English offline results** label their word list in the interface language (`local-dict.js`, `posName("e")`).
  4. **Page translation retries** failed batches after 5 s, 20 s and 60 s. After that the bar shows "Some parts couldn't be translated" with a retry button (`page-translator.js` `retryLater` / `retry`, state `failed`; `content.js` `renderPageBar`, action `"retry"`).
  5. **Translations keep paragraph breaks** (`background.js` `tidyText`). Text over 4,500 characters is translated in pieces (`splitLong`, `longLookup`) instead of being cut at 5,000. Over 30,000 is refused with `too_long`, which the card, popup and clipboard tools explain. The card sends `info.raw` for sentences, and the translation areas use `white-space: pre-line`.
  6. **The Settings review summary** returns to the introduction when the deck is empty.
  7. **Right to left for Persian, Urdu and others:** one shared `LamhaI18n.textDir()` used by the card, the popup and the page translator. Also, **milestones count words**: `addHistory` resolves to whether the word was new, and a word already in the recent history isn't counted again.

## Next steps

- 1.8.1 was released after `npm run smoke` passed 63/63 on the work PC (the Ollama check is optional and skipped there).
- Desktop dependencies on a PC with npm 11: `npm ci` skips install scripts, so afterwards run `node node_modules/electron/install.js` and, in `node_modules/koffi`, `node ./cnoke.cjs -P . -D src/koffi --prebuild --release`. CI uses Node 22 (npm 10), which doesn't need this.
- Check by hand in Firefox:
  - Translate a multi-paragraph selection and confirm the paragraphs survive. Google's `single` endpoint couldn't be checked from the command line (it showed a bot page); the batch endpoint was confirmed to keep `\n`.
  - Look at the page bar's "partial" state, e.g. by going offline during a page translation.

## English–English dictionary (2026-09-27, released in 1.8.2)

The user asked for English–English word lookups. They chose a switch on the card, remembered, plus a setting; and review cards showing both meanings.
- **Setting:** `enDict` (storage.sync, default false). The card's **العربية ⇄ English** switch (`dictSwitch` in content.js, shown on English word cards when the translation language isn't English) sends `setWordDict` to the background, because the desktop card can't write settings itself. Settings → Dictionary has the same switch.
- **Lookup:** `lookup()` computes `english` (an English word, and `enDict` on or translation language English) → `englishLookup()`: `LocalDict.lookupEnglish()` first (definitions without Arabic, the sense that fits the sentence as `translation`, `heroExample`, `contextSense`, `ar` = the Arabic meaning with a fallback to the word's usual one), then Google's English definitions (`onlineLookup` with `tl` = the translation language, so `ar` comes from it). Results carry `mode: "en"`. No sentence is sent anywhere for English lookups (no `contextTranslate`).
- **Cards:** `cardFromLookup` / the card's bookmark / the clipboard's review tool make `{ tr: ar, def: definition, en: true }`. `putCard` accepts an English card with only a definition. The popup's review shows `def` first for `en` cards.
- **Tests:** 3 in test-writing (with the real dictionary: `makeEnv({ realDict: true })`) and 2 in test-ui. All fail on 1.8.1.

## After 1.8.2: fixes from the user's screenshots (2026-09-27, released in 1.8.3)

1. **The English view picked the noun for "I mentioned"** while the Arabic view (Google's in-sentence translation) had the verb. `local-dict.js` `toResult` now chooses with three signals:
   - words shared with the sentence (as before);
   - `posHint()`: the grammar of the word and the word before it (a determiner → noun; a pronoun, "to" or a modal → verb; -ed, and -ing after a helping verb → verb);
   - online, `ctxAr` (Google's Arabic for the word in its sentence, from `contextArabic()` in background.js, at most 2.5 s) matched to the senses' Arabic with `arMatch()`. The labels of matching Wiktionary senses also favour WordNet definitions that share their words (البنك → "institution" → "a financial institution").
   - The leading definition is `bestDef`; the English view shows its part of speech (`heroPos`) above it. Google's own English definitions follow `posHint` too.
2. **The card's top bar:** the "Offline dictionary" / AI badges are icons with the name in a tooltip (`.badge.icon`); the bar stays on one line; with the العربية ⇄ English switch, only the Lamha logo shows.
3. **The clipboard tools view** (Alt+Shift+V → Tab), redesigned in `clip-actions.js` / `clipboard.css`:
   - a header with a back arrow, "أدوات النص" and a clip card (3 lines; source · time · language);
   - numbered rows with an icon and a short hint; 1–9 (or ١–٩) run a tool;
   - the mouse moves the keyboard focus, so only one row is lit;
   - an answer takes the menu's place; Esc steps back (answer → tools → list), and the arrow goes straight to the list;
   - a key-hint bar at the bottom; the panel's own header is hidden while the tools show.
   - The self-test now clicks `.ca-back`.
- **Tests:** 3 in test-writing, 1 new and 2 extended in test-ui. All fail on 1.8.2. The desktop self-test passed 63/63 before the release.

## After 1.8.3: the Theme setting everywhere (2026-09-27, released in 1.8.4)

The user reported that Settings stayed dark after choosing Light: the Theme setting only reached the card, and every page followed the system. Now:
- `shared/theme.js` (loaded in `<head>` of popup.html and options.html, and injected into the clipboard panel) sets `data-theme` on `<html>`, with a localStorage copy for the first paint.
- `shared/ui.css`, `popup.css` and `motion.css` use their dark rules for `[data-theme="dark"]`, or `:not([data-theme="light"])` inside `prefers-color-scheme: dark`.
- The desktop app sets `nativeTheme.themeSource` (`applyThemeSetting` in main.js), so every window follows.
- Tests: 1 in test-ui (fails without the CSS change), plus a desktop self-test check (passed: 64/64 before the release).

## Earlier project: translation that works without internet (built; the hand checks below are still open)

**Goal (from the user):** the app, especially the Windows app, should keep working with no internet at all.

**Decisions so far:**
- Add a translation service choice: Google / AI / Automatic (Google first, AI when offline or rate-limited).
- The provider is **flexible**: translation picks its own AI provider, separate from the writing tools (e.g. writing on Gemini, translation on Ollama).
- Page translation may use AI too, as an option.

**Step 1: measure quality first.** `tools/compare-translation.mjs` runs 13 sentences and 5 word-in-context cases through Google, both Gemini models and Ollama, and writes `translation-report.md` (git-ignored). The user has a Gemini key on the work PC and Ollama on the home PC, so each part runs where its provider is.

**Gemini results (2026-09-27, free tier):**
- **Google:** about 0.3 s and fully reliable. Everyday, formal, news and technical text are fine. It fails on idioms ("piece of cake" word for word), slang ("lit" → lit up, "no cap" → no lid) and ambiguity ("her duck" → her bird). All 5 word-in-context cases were right.
- **gemini-3.8-flash:** the best Arabic by far ("لكل حادث حديث", "مبنى الركاب", the most polished paragraph, ambiguity right). But it returned 503 "high demand" and then **429 quota exhausted after about 11 requests**, and takes about 4–6 s per answer (the report's 20–90 s include the script's waits between retries). Too limited for translation volume.
- **gemini-3.5-flash-lite:** about 1 s, never failed, and better than Google on idioms, formal register, ambiguity and "a cold" (زكام). But it **mixed foreign letters into Arabic twice** (Korean "캐시" in the technical sentence, Hebrew "רא…ית" in "رأיתها"), slipped into dialect on the slang sentence, got "no cap" wrong, and turned "moved to" into "postponed".
- **Conclusion:** keep Google as the fast default. Use AI as the offline / failure fallback and as an on-demand "better translation". Any AI translation needs a **foreign-script guard** (letters outside Arabic/Latin in Arabic output → retry, then fall back). Page translation on free Gemini will hit the quota; offer it only as an opt-in with a warning.
- **Ollama run:** skipped for now at the user's request. A small local model is expected to be below Flash-Lite, and more prone to the foreign-script problem.

**Step 2: built (2026-09-27, committed and pushed).** All four test files pass: 42 + 23 + 64 + 8. The lint is clean.
- Settings → Translation has Automatic (default) / Google only / AI; its own provider ("same as writing tools" / Ollama / Gemini / Claude) and model; and "Translate pages with AI too" (off by default).
- Default translation models: Gemini Flash-Lite, Claude Haiku, the writing tools' Ollama model.
- Words always try Google first (its dictionary data). "Local only" still allows a local AI (Ollama) for sentences and word-in-context, never an online one.
- The card: an AI badge on AI answers; a "✨ Better translation" link under Google's sentence translations (`lookup` with `engine: "ai"`); AI errors (quota, key) explained with a Settings button.
- AI answers with stray letters are asked again, then Google is used. A rough answer is shown only when nothing else works, and it's never cached.

**Next steps for this project:**
- Try it by hand in Firefox and in the Windows app with the Gemini key: Automatic with the Wi-Fi off, the Better translation link, page translation with AI on.
- Maybe: the popup's quick-translate box could show the AI badge and offer "Better translation" too (only the card has it).
- Maybe: an automatic fallback for the **writing tools** (e.g. Gemini, then Ollama when offline), which the same `aiProviderConfig` design allows.
- Later: the Ollama quality run on the home PC, to tune the Ollama defaults.

**Everything in the desktop app that needs internet today, and the offline answer:**
- Words: the offline dictionary ✅.
- Sentences, pages and word-in-context: need Google → AI translation through Ollama (this project).
- Arabic versions of English definitions for local results (`fillGlosses`): Google; offline shows English only.
- Pronunciation: Google's voice, falling back to Windows voices (works offline if the voice is installed).
- Wikipedia and updates: online only, and they fail quietly.
- Writing tools: one provider at a time today. An automatic fallback (e.g. Gemini, then Ollama when offline) would fit the same design.
- Truly offline (no network at all) needs Ollama **on the same PC**. Over the home network, Ollama must listen on the LAN (`OLLAMA_HOST=0.0.0.0`).

## Design audit and polish (2026-09-27, version 1.9.0)

The user asked for a thorough design audit to make the interface consistent and fun to use. Plan, agreed with the user:
1. **Quick visible fixes.**
2. **One design system:** the card (`content/styles.js`) and the pages (`shared/ui.css`) have drifted apart: different dark colours, a hard-coded dark toast in both themes (`.flash`, `.saved`, `.lc-toast`), bright periwinkle main buttons that outshine the content in dark mode, a muddy olive milestone banner, emoji in Settings (📖🌐✨💻📋) next to line icons, black "wells" behind them, no spacing / radius / type scale, two designs for the clipboard list (panel vs tab).
3. **Layout:** the main window is ~60% empty below its card; Settings' section links are cut off with no hint, long intro paragraphs, tiny danger buttons; Review's front side is mostly empty.
4. **Fun:** a daily goal and streak, a word of the day in the empty main window, a livelier review (progress, end-of-session summary), micro-interactions (bookmark pop, copy → ✓, sound waves while speaking). All through `LamhaMotion`.

**Tool:** `npm run shots` (`desktop/scripts/screenshots.js`, started by `main.js --screenshots`) takes the before/after pictures with a temporary profile, sample text and no API keys (writing tools show their setup state). It never touches the real clipboard: sample clips go straight into the store. AI results, Firefox's popup and real web pages need screenshots from the user.

**Step 1: quick fixes.**
- **"false" as text** in the sentence loading card ("falsefalsefalse") and in the finished page bar: `cond && h()` passed to the DOM's own `.append()`. Both filtered (`renderSkeleton`, `renderPageBar`); a scan found no other place.
- **Scrollbars in the theme:** the card's shadow root had no `color-scheme`, so its scrollbar was white on the dark card. `.root` / `.root.dark` set it, plus `scrollbar-color: var(--scroll)`, also on the pages (`ui.css`).
- **Arabic placeholders** sat on the left (an empty `dir="auto"` box is left to right): `:placeholder-shown { direction: inherit }`.
- **Writing tools before an AI is chosen:** the popup's Write tab shows a neutral setup box as soon as it opens (was red, and only after a click), the tools stay disabled, and they wake up when Settings changes; the card's title no longer names a provider and its chips are disabled.
- **English–English view:** the definition in the top box isn't repeated as number 1 below it.
- **One focus mark:** no outline on top of the popup text boxes' and the clipboard search's own focus ring; a selected tool chip gets a soft halo instead of a second ring.
- Tests: 3 in test-ui, each failing on the old code.

**Step 2: one palette.**
- New shared tokens in `shared/ui.css`, copied into the card's `.root` / `.root.dark` (`content/styles.js`, whose `--bg` is the pages' `--surface`): `--btn` / `--btn-fg` (filled buttons, selected chips, badges: #5e5ce6 with white text in dark, calmer than the periwinkle `--accent`, which stays for links, labels and icons), `--ok-soft`, `--danger-soft`, `--toast-bg` / `--toast-fg` / `--toast-act` (toasts invert with the theme, like the card's pill), `--celebrate` (milestones in the Lamha gradient; amber went olive on dark).
- A test in test-ui checks the card and page palettes match and the two dark blocks in ui.css are identical. **Change a colour in all three places.**
- Corrections (`ins`) look the same everywhere: green text on `--ok-soft`.
- Line icons, not emoji, in structural places: Settings' choice cards (on an `--accent-soft` tile), section titles, the Write tab, the clipboard's pin marker and opt-in. Emoji stay in celebrations (🎉 milestones, 🔥 streaks, 👋 welcome), warnings (⚠️) and prose.
- The clipboard tools' lit row lost its edge bar (tinted row + filled icon, like a selected chip).

**Step 3: layout.**
- Corners: three steps, `--r-sm` 8 / `--r-md` 10 / `--r-lg` 14 px (plus 999px pills and 2–6 px marks), in ui.css and the card (checked by the palette test). The old `--radius` is gone.
- Main window (desktop): the footer sits at the bottom (`desktop.css`, body a flex column); recent lookups show 12, the Firefox popup still 6 (`.hist li:nth-child(n+7)`).
- Review: the word is centred in the card; "due" and "new" are the same pill in their own colours, quiet at zero.
- Settings: the section links scroll in an inner `.toc-links` row that fades where more are hidden (`tocEdges`); the clear/delete buttons have a trash icon and a minimum width.
- Clipboard panel: four key hints on one line (Enter, Shift+Enter, Tab, Esc); pin and delete keys are in the row buttons' tooltips.
- Not done: a type scale (text still in half-pixel steps, e.g. 12.5 / 13.5 px); rewriting Settings' long intro paragraphs (the user's own copy: ask first).

**Step 4: fun.** The user chose: the goal counts both lookups and review answers, 10 a day, and the word of the day mixes their own due words with new dictionary words.
- **Today** (`background.js` "today" section): storage.local `activity` = `{ [toDateString()]: count }` for 60 days. `countActivity()` runs for each word new to the history (the same test as milestones) and each review answer, and returns the goal on the one that reaches it. `lookup` results then carry `goal`, and `reviewGrade` answers `{ ok, goal }`. `streakOf()` counts days in a row with any practice (today not started yet doesn't break it). Setting `dailyGoal` (sync, default 10, 0 = none) in Settings → Review.
- **Word of the day** (`wordOfDay()`, kept in storage.local `wotd` for the day): a reviewed card due within 3 days (not yesterday's word), else `LocalDict.wordOfDay(dayNumber, known)`, which picks from positions 40–400 of a shard (shards keep frequency order), with Arabic and an example that uses the word itself.
- **Popup:** a Today card in the ترجمة tab (`renderToday`: SVG ring, streak chip, the word with 🔊 and "see its meaning"), hidden while the query box has text. Recent lookups now also refresh on `history` changes (the desktop window stays open while the card adds words).
- **Review:** a progress bar for this sitting (`rvSession`, `#rvProgress`, a `scaleX` transform) and a summary on the done screen (`rv.session`). Reaching the goal: a flash plus a burst.
- **Micro-interactions:** copy → ✓ for 1.3 s (card `copyText(text, btn)`, popup `copyFrom`); the bookmark pops when a word goes in; speaking pulses the 🔊 icon's sound waves (`.playing`, opacity only; the old box-shadow pulse is gone).
- Tests: 3 in test-writing, 4 new plus 3 extended in test-ui.

**Before 1.9.0 goes out** (the version is bumped and committed, but not pushed or tagged):
- ✅ `npm run smoke` passed 64/64 on the home PC (2026-09-27), after the version bump.
- Check by hand in Firefox: the Today card in the 360 px popup, the card's dark scrollbar, and Arabic placeholders on the right.
- ✅ Pushed, and `v1.9.0` tagged on the release commit (2026-09-27), before the Firefox check (the user's call).

## After 1.9.0: Settings alignment, from the user's screenshots (2026-09-27, released in 1.9.1)

1. **Buttons next to text boxes sat 12 px low** (Save & verify, Refresh list, Add, Test): options.css had a generic `.small { margin-top: 12px }` meant for `p.muted.small` notes, which also hit every `.btn.small`. Now `.muted.small`. Text boxes, dropdowns and the buttons beside them share `--control-h` (36 px, ui.css).
2. **Clipboard → Clear unpinned / Clear all** look like deleting now (`.btn.small.danger` with a trash icon), like Settings' other clear buttons.
3. **The desktop section bar listed Firefox-only sections** ("How it appears"): desktop.css hides those sections, so it now hides their links too (`.desktop .toc a[href="#triggerPanel"]`…). `tocEdges` ignores hidden links (they faded the first real link).
4. **"Privacy & history" read "Privacy history"** in the section bar: the link text removed every non-letter to drop emoji; now it removes only emoji (`\p{Extended_Pictographic}`). Test in test-ui (fails on the old code).
- `npm run shots` also stops at the clipboard's excluded programs and clear buttons (`cbAppForm`).
5. **Filled buttons' keyboard focus** is a soft halo (`.btn:focus-visible`, 45% of `--btn`), not a second ring: the clipboard tools focus لصق so Enter pastes.
- `npm run smoke` passed 64/64 before the release.
6. **`npm run shots` uses Ollama** when it's running (the user asked, so the pictures show the writing tools working): it picks a qwen model like the self-test, warms it up, then captures the card's Proofread and Improve (real key presses: `sendInputEvent`), an AI sentence translation, the Write tab's Proofread and Improve, and the clipboard panel's Proofread. The card can't be read from outside (closed shadow root), so it waits a measured time; the pages are polled. The corrections come in one after another, so it waits 1.8 s before each picture.

## After 1.9.1: alignment and Longer, from the user's screenshots (2026-09-28, released in 1.9.2)

The user cares a lot about alignment ("it feels off if something isn't aligned"). Check spacing and edges in every new screen.
1. **Write new and Reply: a "Longer" tone** (`long`, مفصّل) next to Short. background.js has one `TONES` table (`toneOf()`, checked with `Object.hasOwn`); a message is "short" in the prompt unless Longer is picked.
2. **The Settings section bar slid sideways under the mouse** (the user's GIF): as the page passed each section, the bar scrolled to keep the marked link in view, so the link under the pointer kept changing. Now the row only scrolls when the pointer isn't on it (`bringIntoView`, the row itself via `scrollBy`, never the page), and a clicked link stays marked for 1.2 s while the page gets there.
3. **Dividers glued to what's above them**: `.opt` draws its line at its top edge, so after choice cards or a text box row it touched them. `.panel :not(.opt):not(h2) + .opt` (and the first row of a box after `.choices`) gets 14 px above the line. Hidden siblings count too, so the Writing tools row after the Ollama box isn't glued either.
4. **Updates is the last section** (desktop), after Privacy & history; only the dictionary credits follow. The self-test checks it.
5. **Animations → Preview** sits under its note in a column with an 8 px gap (`.md-side`); it had relied on the stray `.small` margin removed in 1.9.1.
- Tests: compose tones in test-writing; Longer on the card and the section bar's click in test-ui (each fails on the old code). `npm run smoke` passed 64/64 before the release.
6. **Longer (`expand`, أطول) for selected text**, right after Shorter: on the card (key 6: Summarize, Explain and Reply moved to 7–9) and in the popup's Write tab. Replace works (in `REPLACEABLE`). Tests in test-writing and test-ui.
   - Tried with Ollama qwen3.5:4b: it lengthens messages well ("cant make sunday meeting" became a polite note asking for another time), but leaves a plain statement ("I went to the store…") almost as it was, and sometimes adds a vague reason ("a prior commitment") despite the prompt. Naming such phrases in the prompt made it *use* them and invent a story, so the prompt only asks for [placeholders] instead of facts. Gemini and Claude should follow it better; check when one is at hand.

## "Read more on Wikipedia" on the left (2026-09-28, released in 1.9.3)

- In the English interface the link sat on the right (`.wiki` is right to left so the picture sits on the right). `.root.en .wiki a` in `content/styles.js` moves it to the left, the icon still before the text. The user checked it.

## The Write button on a double-click in an empty text box (2026-09-28, released in 1.9.3)

**Goal (from the user):** double-click an empty text box and the ✨ Write button appears (like the lookup button on a selection), opening *Write new* for that box. Mainly for the Windows app, to write straight into any program.

**Decisions:** on by default; only empty boxes (a box with text already has Alt+Shift+W for all of it); Write new guesses Email / Message from the site or app; a one-time tip explains the button. In the Windows app, **skip a browser only when it has the Lamha extension** (any browser, not only Firefox: extensions for other browsers are planned), and work normally in browsers without it.

**Part 1: the browser extension (done; all tests pass, lint clean).**
- `content.js`: a `dblclick` listener → `emptyEditable()` (textarea, `input type=text`, or an empty contenteditable; never password, email or read-only) → `showPill({ ...composeInfo(), aiOnly: true })`: one button, aria-label `c.writeNewHere`. Needs `aiReady`, `isActiveHere()` and the `writeOnDblClick` setting (storage.sync, default true, in all three defaults; Settings → Writing tools).
- `evaluate()` and the `selectionchange` handler leave this pill alone (its own pointerup would hide it). Typing, a click elsewhere or Esc hides it. Opening it passes `focus: true`, so Esc puts the caret back in the box.
- `composeInfo()` sets `kind`: `"email"` on webmail hosts (`MAIL_HOSTS`) or when `msg.kind === "email"` (for the desktop app), otherwise `"message"`; `composeForm()` starts from it.
- The tip (`.pill-tip`, `c.writeTip`) shows once per device: `writeTipSeen` in storage.local, written through the background because the desktop card can't write storage.
- Tests: 3 in test-ui (`dblClickIn()` helper; jsdom has no `execCommand`, so the Insert test stubs it). All fail on the old content.js.

**Part 2: the Windows app (built; unit tests pass, the desktop self-test's two new checks haven't run yet).**
- **Noticing the double-click:** a `WH_MOUSE_LL` hook in the **helper process** (uia-context.js), C# compiled by Add-Type the first time it's needed (about 0.5 s), on its own thread with its own message loop. Its callback only queues left-button presses; a writer thread prints `{ down: { t, x, y } }` lines. main.js pairs them with `ClickPairer` (double-click.js) and Windows' own `doubleClickZone()` (native.js). Not Raw Input in main.js as first planned: that sends a message for every mouse *movement* into Lamha's main process. Not a hook in main.js either: every click on the PC would wait for its main thread.
- **Empty box?** The helper's `field` question: the focused element is Edit/Document, enabled, not a password, not read-only (ValuePattern, or TextPattern's IsReadOnly attribute), under the click (the helper is per-monitor DPI aware so hook points and UIA rectangles agree), and its text empty or whitespace. Web content (FrameworkId Chrome/Gecko) must be inside a Document, which also leaves out the browsers' address bars (`web: true`).
- **The extension's button:** for web content the helper waits until ~350 ms after the second press, then looks for a button named `c.writeNewHere` in either language (`LamhaI18n.pair`) among the document's last 4 children (`lamha-ui` is the page's last element). Found → `lamha: true` → no app button. Works for any browser, no per-browser code.
- `wantsButton()` (double-click.js): never Lamha itself or Windows' shell (`SHELL_APPS`); browsers (`BROWSERS`) only in the web page. `composeKind()`: `EMAIL_APPS` (Outlook, the new Outlook `olk.exe`, Thunderbird…) → email.
- **The button:** `showWritePill()` in main.js places the card window with the mouse ~72 px from its top (`placeCardWin({ pill: true })`), `showInactive()`, and sends `showWritePill` (content.js draws the same pill, `composeInfo(msg)` gives `kind` and an `external` editable). The window's `focus` event means the button was clicked. It hides after 5 s (not while the mouse is on it), on any other click (the hook's presses; `cardHover` from preload's hover messages), or when a shortcut opens a card.
- **When the hook runs:** `startContextHelper()` keeps the helper for `useContext` or the double-click button (`writeOnDblClick` and a writing-tools AI ready); `watchDoubleClicks(on)`. The helper restarts itself (up to 5 times) if it dies while the hook is wanted.
- Tests: `tools/test-desktop.mjs` (new, in `npm test`): the pairer, `wantsButton`, `composeKind`, `pair`, and on Windows: the script parses, the C# compiles, the helper answers. test-ui: the card's `showWritePill`. Desktop self-test: a real double-click in the WinForms test app (button shown, the app keeps the focus, Insert writes there), and none over text.
- `desktop/package.json` `files` lists `double-click.js` (the packaged app only has what's listed).

**Next steps:**
- **The user reports (2026-09-28, after 1.9.3) that the Windows app's button doesn't show in the many apps they tried** (the browser extension's does). Parked at their request; the self-test's WinForms box passes, so start with the development build's `[write button]` log lines in those apps (is the hook's double-click seen at all? what does `field` answer?).
- Idea: let the extension and the app talk directly (native messaging), so the app knows exactly when the extension handles a box; later they could share settings, the deck and history. Needs the app registered in Windows and a new extension permission.
- ✅ `npm run smoke` passed with both new checks (67/67).
- Try by hand: Notepad, WhatsApp, Outlook (should start as Email), Word; Firefox with the extension (only one button), Chrome without it (the app's button, not in the address bar); a scaled screen (125–150%).
- Known: Chromium apps turn their accessibility on at the first UI Automation question, so the very first double-click in Chrome/Edge/Slack may show nothing.
- Ideas the user may want: "don't show in this app" from the button; remember the last kind/tone per app; "Paste as English" when the clipboard has Arabic.

## Words explained in their own language, for every language (2026-09-28, released in 1.9.3)

**Goal (from the user):** the English–English dictionary only worked for English. A word in the translation language itself (French with French selected) came back as itself, and Arabic had no Arabic–Arabic view. The user asked what the English–English switch does, agreed with generalizing it, and asked about offline dictionaries for other languages.

**Built (all tests pass, lint clean):**
- **The rule** (`explains(lang, tl, settings)` in background.js): a word is explained in its own language when that language is the translation language (automatic), or when the user turned it on for that language: `enDict` for English (kept, older versions on other devices read it), `explainLangs` (storage.sync, array) for the others. `setWordDict` now takes `{ lang, on }` (the old `{ en }` still works) → `setExplain()`.
- **The mode is renamed** `mode: "en"` → `mode: "explain"` everywhere (background, local-dict.js, content.js, popup.js, clipboard-actions.js, tests). Explained results: `tl` = the explanation language, `other` = the translation language the card's switch goes back to, `ar` = the meaning in it.
- **lookup():** English words keep `englishLookup()` (offline dictionary first). Its Google fallback now asks with `sl: "auto"` and returns null when Google says the word isn't English ("maison" with enDict on was sent as English before). Any other word: the normal translation first, then `explainWord()`: Google's definitions (in the word's language) when there are some, else `aiExplain()` with the translation AI (`tr.ai`; on "Local only", Ollama only). Nothing to explain with: a same-language word gets `translation: ""` + `explainMissing`; a foreign word keeps its translation + `explainMissing` (the card and popup say "choose an AI provider").
- **aiExplain():** JSON schema (`EXPLAIN_SCHEMA`): up to 3 senses (pos from a fixed list → the offline dictionary's part-of-speech labels), gloss, example, synonyms; `meaning` in the other language; `root` and `plural` for Arabic. The sentence goes along when there is one (its sense first). Letters from another script (`strayLetters`) → asked again, then refused.
- **Card:** the switch shows on any word whose language differs from the one it translates into (`trLangOf`): labels `[translation language, word's language]`. Definitions, examples and synonyms take the word's writing direction (`defDir`; right-to-left styles `.def .ex[dir="rtl"]`, `.def .syn[dir="rtl"]`); Arabic shows `.hero .roots` (الجذر · الجمع). Strings: `c.noDef`, `c.noExplainAI`, `c.root`, `c.plural` (`c.noEnDef` removed).
- **Settings → Dictionary:** "شرح الكلمات بلغتها" with a toggle button per language (`#explainLangs`, `.lang-toggles`), redrawn when the card's switch changes the list.
- Also fixed: Google's definitions of a non-English word were translated as if English (`translateBatch(..., "en")` in onlineLookup → the word's language).
- Tests: 6 new in test-writing (French same-language, the per-language switch, "maison" isn't English, Arabic by the AI with root/plural, stray letters, nothing to explain with); all fail on the old background.js. 3 new in test-ui (Arabic card right to left + switch, the "can't explain" note, Settings toggles).

**Not checked:** Google's `single` endpoint can't be reached from the command line, so "Google gives definitions in the word's own language for French/Spanish/German…" comes from the Translate website's behaviour, not a test against the real service. Try by hand: French with French selected, the switch on a French and an Arabic word (with Gemini or Ollama for Arabic).

**Offline dictionaries for other languages (the user's question):** kaikki.org has Wiktionary editions written in French, German, Spanish and Turkish (among ~20), so French–French etc. could be built like dict/ (tools/build_dict.py), filtered to common words: roughly 5–15 MB each, too big to ship to everyone → **optional language packs** downloaded from Settings (idea, not started). No Arabic, Persian or Urdu edition there; classical Arabic dictionaries are free but archaic and hard to parse, so Arabic–Arabic stays with the AI (offline with Ollama).

## Downloadable language packs (2026-09-28, released in 1.9.3)

**Goal (from the user):** offline dictionaries for the other languages ("start on those packs").

**Built (all tests pass, lint clean):**
- **Builder:** `tools/build_packs.py <lang>` streams kaikki.org's `https://kaikki.org/dictionary/downloads/<lang>/<lang>-extract.jsonl.gz` (the Wiktionary edition *written in* that language), keeps headwords of that language with Zipf ≥ 2.0 (wordfreq), the 40,000 most common; up to 4 senses (every part of speech gets one first: `pick_senses`), one example each, synonyms, IPA; inflected forms with Zipf ≥ 2.5 (from `forms` and "form of" entries). Output `dist-packs/<lang>.json.gz`: `{ meta, shards: { key: { w: {word: {s, p}}, f: {form: lemma} } } }`, key = `shard_key()` (two letters, lowercase, accents stripped; `shardOf()` in packs.js must match).
- Built 2026-09-28: **tr** 33,902 words, 1.8 MB · **es** 37,539 words, 3.3 MB · **de** 40,000 words, 5.2 MB · **fr** 40,000 words, 5.2 MB (the French source is 730 MB compressed and took a few minutes to stream). Spanish and French Wiktionary list few plurals, so `find()` also tries the word without -s/-es (-x in French) when that's a headword.
- **packs.js** (`LamhaPacks`): `CATALOG` (size, words: fill in after building), `RELEASE` = `https://github.com/osamaomer/lamha/releases/download/packs-v1/`, `install()` (fetch → progress → DecompressionStream → checks `meta.lang`/`FORMAT` → stores shards, meta last), `remove()`, `list()` (with `progress`), `find()` (exact, lowercase with the language's rules (Turkish İ), capitalised for German nouns, then forms, then plurals). Words are untrusted keys: `Object.hasOwn`.
- **background.js:** `explainWord()` asks the pack before Google's definitions and the AI; `packLookup()` answers offline (Local only, or no network, or Google failing), wanted languages first, then any installed pack; `packResult()` builds an explained result (`source: "local"`, the offline badge; `inflected` for forms). The lookup cache key includes `LamhaPacks.key()`.
- **Settings → Dictionary → قواميس للتنزيل:** a row per language (words · MB, Download / Remove (danger style), progress while it downloads, polled every 300 ms, errors explained).
- **Desktop:** `desktop/pack-store.js` (files in `%APPDATA%\Lamha\packs\<lang>\<key>.json`; only pack keys become file names), set as `globalThis.LamhaPackStore` in `startCore()`; `packs.js` added to the loaded scripts, `sync-ext.mjs` ITEMS and `package.json` files.
- Tests: test-writing 3 (install/find/forms/offline/remove, a pack before the AI with Google's translation under it, refused downloads), test-ui 1 (Settings list), test-desktop 2 (the file store and its key checks; the real `dist-packs/tr.json.gz` when present).

**Next steps:**
- **Publishing:** `.github/workflows/packs.yml` builds the packs on GitHub's servers and publishes them when a `packs-v*` tag is pushed (`git tag packs-v1 && git push origin packs-v1`; `gh` isn't installed on the work PC). The release is created with `--latest=false`: the Windows app's updater reads the *latest* release's latest.yml, so a packs release marked latest would hide app updates. CI builds from kaikki.org's data of that day, so sizes can differ a little from `CATALOG` (only shown before downloading; the download itself uses Content-Length).
- Try by hand: download one in Firefox and in the Windows app, then look words up with the Wi-Fi off.
- Maybe later: pick the sense that fits the sentence (the English dictionary does; packs show the first), and bigger packs as an option.

## Before 1.9.3 went out: the desktop self-test found three things (2026-09-28)

`npm run smoke` passed **67/67** only after these; the first run failed the real double-click check.
1. **A real bug:** the card window hides on blur but the card stays in the page; the next double-click's `showWritePill` closed that old card, and closing tells the app the card is gone (`lamhaDesktop.closed()` → `hideCard`), so the fresh button vanished ~0.1 s after it appeared. `closeCard({ notify: false })` now leaves the window alone; test in test-ui (fails without the fix).
2. **Classic Windows text boxes** (Edit, RichEdit, WinForms' EDIT) can describe themselves badly to UI Automation: the self-test's WinForms box is a "Pane" with no patterns. `native.focusedTextBox()` asks Windows directly (GetGUIThreadInfo → the focused control's class, ES_READONLY / ES_PASSWORD, WM_GETTEXTLENGTH; never the text) and main.js uses it before UI Automation.
3. **Two self-test traps** (not bugs in Lamha): a PowerShell window started with `windowsHide` is invisible yet "in front" (keys work, clicks land behind it), so `startTargetApp(text, { visible: true })`; and a click sent from Electron's main thread waits for Electron's own low-level mouse hook (setIgnoreMouseEvents with `forward`), which lives on that thread, so `clickFromOutside()` sends clicks from a short-lived `ELECTRON_RUN_AS_NODE` process.
- Development builds log each double-click decision (`[write button] …`: program, text box or UI Automation, the flags; never the text).
- This PC's screen is scaled to 150%: the hook's points and UI Automation's rectangles are physical pixels; only DPI-aware code gets matching window rectangles.

## What's new, in the Windows app's Settings → Updates (2026-09-28, released in 1.9.3)

The user asked for a way to read the changelog inside the app, in the Updates card, before 1.9.3 went out.
- `shared/changelog.js` (`LamhaChangelog`: `{ v, date, notes: [[ar, en], …] }`, newest first) has every version from 1.5.0; 1.8.1 on in detail, older ones from their release commits.
- `desktop/renderer/clipboard/settings.js` `renderNews()`: "What's new in version X" with its date, the notes in the interface language, earlier versions in a `<details>`; "All releases on GitHub" stays as a link. main.js injects `shared/changelog.js` with settings.js. Styles `.up-news`, `.up-notes`, `.up-older` in clipboard.css.
- `tools/release-notes.mjs <version>`: Markdown for the GitHub release (English, then Arabic in a `dir="rtl"` block); release.yml uses it with `--notes-file` instead of `--generate-notes`, and fails when the version has no entry.
- Tests: test-desktop (well-formed, newest first, the manifest's version has notes, the notes' Markdown). Desktop self-test: the Updates card shows the notes (passed). `npm run shots` now includes the Updates section.

## Offline Wikipedia (2026-09-28, stages 1–3, released in 1.9.4)

**Goal (from the user):** full offline support: download Wikipedia (Kiwix's `.zim` files, https://dumps.wikimedia.org/kiwix/zim/wikipedia/) from Settings, totally optional, and read it in Lamha's own design. **Agreed plan:** Windows app first; stages 1–3 now (reader, Settings, the card offline), then 4 (Lamha's own reader window) and 5 (Firefox asks the Windows app over native messaging, the same connection as the double-click idea). Files go to `%APPDATA%\Lamha\wikipedia` by default, with a Change folder button.

**Facts found:**
- Kiwix's catalog: `https://library.kiwix.org/catalog/v2/entries?lang=ara&category=wikipedia&count=500` (OPDS; ISO 639-3 languages; `name` wikipedia_ar_top, `flavour` mini/nopic/maxi, `articleCount`, the acquisition link is `<file>.zim.meta4` with `length`). Each file has `.sha256` and `.meta4` (its mirrors) on download.kiwix.org.
- Sizes (Sept 2026): Arabic top (231k articles) mini 226 MB / nopic 926 MB / maxi 2.7 GB; all mini 3.2 GB / maxi 19 GB. English top mini 297 MB; all maxi 127 GB. Turkish has no "top", but all mini 1.2 GB and small topic sets.
- **Mirrors differ enormously**: from this PC the mirror download.kiwix.org chose (ftp.nluug.nl) gave ~0.4 KB/s, dumps.wikimedia.org ~900 KB/s. Hence the downloader tries each mirror briefly and takes the fastest.
- Current files are ZIM 6.3: content in namespace "C", Zstandard clusters, **no header title list** (`titlePtrPos` is all ones): titles are in `X/listing/titleOrdered/v1` (redirects included). Node 24 (Electron 44) has `zlib.zstdDecompress`, so the reader needs no dependency. Kiwix's own libraries are GPL; Lamha is MIT, so it has its own reader. Old xz files (before 2021) are refused ("too_old").
- mwoffliner's HTML: the lead is `<section data-mw-section-id="0">`; ordinary articles link their disambiguation page with class `mw-disambig` (not a disambiguation mark!); real ones carry `mw:PageProp/disambiguation`. Pictures are `./_assets_/<hash>/<name>` (webp even when named .jpg), in stored (uncompressed) clusters.

**Built:**
- `desktop/zim.js` (`ZimFile`): open (header checks), `find(ns, path)` (binary search), `resolve` (redirects, at most 10), `content`, `titlesStartingWith`, `meta`, `main`; LRU caches for entries and 6 decompressed clusters; every offset checked (`ZimError` codes).
- `desktop/wiki-library.js` (`WikiLibrary`): `wikipedia.json` state; `catalog(lang)` (1 h cache); `download(id)` only for catalog ids → `.part` in the folder, `fastest()` mirror probe (1 MB, 6 s), Range resume, a stalled/broken mirror → the next one, free-space check, SHA-256, then the file is opened and listed; pause/resume/cancel; a download running at quit resumes at the next start (`saveSync`); `addFile` (Wikipedia only, used where it is, id `file:<uuid>`); `remove`; `summary(titles, lang)`: files of that language (all before top before topics; maxi first for the picture), each title as given and capitalised (Arabic also with ال), redirects followed, disambiguation skipped, `leadText()` (first paragraphs of section 0 without boxes, references, coordinates), `canonicalUrl`, a data: URL picture from maxi files.
- background.js `wikiSummary(title, lang, { alt, offline })`: offline → only the downloaded copy; `wikiOfflineFirst` (storage.local) → copy first; otherwise online, and the copy when Wikipedia can't be reached (that answer isn't cached). The translation language's file with `alt` (the word's translations) then the English file.
- content.js: asks for Wikipedia on "Local only" too (`offline: true`), sends `alt` (`wikiTitles()`: the translation split on ، , ; and each meaning's first word); `.wiki-offline` chip "من النسخة المنزّلة · <month>".
- Settings (desktop only, injected after the Dictionary section): the files and downloads (progress bar, speed, time left, pause/resume/cancel, errors), the offline-first switch, Kiwix's list for a language (Top/All × Mini/Full no pictures/Full with pictures, topics folded, "Start here" on Top · Mini when nothing is downloaded), the folder (Change / Open / Default), "Add a file you have". Download buttons are soft (`ghost`), also the language packs' (so the two sections match).
- Tests: test-desktop 10 (a .zim writer for tests: `tools/zim-fixture.mjs`; set `LAMHA_TEST_ZIM` for a real Arabic file: passes with wikipedia_ar_top_mini_2026-07), test-writing 4, test-ui 1, desktop self-test 1 (the real app: a .zim added, the card's answer offline, Settings lists it). `npm run shots` takes Settings' section, and with `LAMHA_SHOTS_ZIM=<file>` the card's offline Wikipedia (scrolled to it).
- Tried on the real Arabic Top mini: باريس, Paris (→ باريس), تفاحة (→ تفاح), شمس (→ الشمس), بنك (→ مصرف), قلب, ماء… ~6 ms each; عين is a disambiguation page (nothing shown).

**Next steps:**
- Try by hand: download Arabic Top · Mini from Settings (watch the mirror choice and speed), pause/resume, quit mid-download and restart, then look words up with the Wi-Fi off.
- ✅ Released in 1.9.4 with its changelog entry; the desktop self-test passed 70/70 before the release.
- Stage 4: built (the next section).
- **Stage 5:** Firefox through the Windows app (native messaging).
- Ideas the user hasn't chosen yet: article of the day in the Today card, reading mode with deck words highlighted, two languages side by side, a Wikidata "bridge" file (English ↔ Arabic titles), Simple English Wikipedia, Arabic Wiktionary as a .zim, read aloud.

## Offline Wikipedia, stage 4: Lamha's reader (2026-09-28, released in 1.9.4)

- **Window:** `openReader({ file, path })` in main.js (one window; asked again, it gets `lamha:wiki-open`). It loads `renderer/wiki/reader.html`, then injects shared ui.css/motion.css, theme.js, i18n.js, motion.js, i18n-desktop.js, `sanitize.js`, `reader.js`, and the content scripts (lamha-ai, styles, page-translator, content.js), so selecting a word gives Lamha's button and card as on a web page. The mouse's side buttons arrive as `app-command` → `{ nav }`.
- **Ways in:** the card's link on a downloaded answer is now *اقرأ المقالة في لمحة* → `browser.runtime.getURL("wiki/?file=&path=")` → `openUrl()` routes `wiki/` to the reader. Settings: *قراءة* per file. Tray: *ويكيبيديا* while a file exists (the tray is rebuilt when the library changes).
- **Safety, three walls:** (1) `sanitize.js` parses the file's HTML with DOMParser (inert) and rebuilds it from an allow-list (tags, a few attributes; no script/style/iframe/form/svg/math, no `on*`, no `style`); links become `data-path` / `data-anchor` / `data-ext` with `href="#"`, and reader.js handles every click; pictures only from inside the file; article ids are prefixed `wk-`. (2) reader.html's CSP: scripts only 'self' (the injected ones aren't subject to it), no connections, images from `lamha-wiki:` / data: / https: (the card's). (3) `lamha-wiki://zim/<file id>/<path>` (`protocol.handle`, registered as a standard secure scheme) serves pictures only (`asset()` checks the MIME type; an article is "not found").
- **Library calls** (`lamha:wiki`): `article(fileId, path)` (redirects followed; no path = the main page; no file = the first that has it), `asset`, `suggest(query, lang)` (titles starting with what's typed, as typed and capitalised; exact first, then shortest; 4× the limit is read so the shortest win), `random(fileId)`.
- **The page (reader.js):** start page (random article, the files with Main page / Random, a link to Settings; with no files, a way to Settings); the bar (home, back, forward, search with keyboard-driven suggestions, contents button on narrow windows, text size A−/A+ kept in localStorage `lamhaWikiSize`, open online); history with scroll positions; a "not in your downloaded copy" toast with *open it online*; contents at the side (≥ 1000 px); Alt+arrows, Ctrl+L or / to search, Ctrl +/− for the text. Lines in the interface's language inside an article (the info line, the mini note, the credit) keep the article's edge (`uiLine`), and suggestions take their title's direction.
- **reader.css:** Lamha's tokens; the info box floats to the article's end on wide windows with a fixed table layout (a nested periodic table had stretched it to 2,000 px and hidden its picture; now it scrolls in its cell); every table scrolls on its own; figures follow Wikipedia's left/right; formulas and diagrams stay readable in dark.
- **Found while building:** lazy-loaded pictures stayed blank (they're local, so no lazy loading); jsdom evals a script with a file-level "use strict" in its own scope (sanitize.js keeps it inside its function); the text size read `Number(null)` as the smallest; one self-test run failed "reads the selection from another app" and passed on the next (the PC was in use).
- Tests: test-ui 4 (the cleaner, the page's search/links/history/toast, text size and the article's edge, no files) plus the card's link; test-desktop 1 (the reader's library calls); desktop self-test 1 (the real window: the picture through lamha-wiki:, nothing from the file runs, links, Lamha's button on a selected word): 69/69. `npm run shots` takes the reader (start page, search, an article, Karbon with pictures from a Turkish maxi file, narrow) when `LAMHA_SHOTS_ZIM` lists files separated by `;`.
- Next: try it by hand (a big article, the mouse's side buttons, the card inside the reader).

## Offline Wikipedia, stage 5: Firefox through the Windows app (2026-09-28, released in 1.9.4)

**The user's choice:** Firefox doesn't download Wikipedia itself; it asks the Windows app over native messaging, which later can also share settings, the deck and history.

- **The host:** Lamha.exe can't be it (`electronFuses.runAsNode: false`, kept on purpose), so `desktop/native-bridge.js` writes to `%APPDATA%\Lamha\firefox`: `com.artworklab.lamha.json` (the host manifest: `allowed_extensions: ["lamha@artwork-lab"]`), `host.bat` (Mozilla's documented Windows form) → Windows PowerShell → `host.ps1`, and `config.json` (pipe name, a random token, how to start the app). Registered at `HKCU\Software\Mozilla\NativeMessagingHosts\com.artworklab.lamha` with reg.exe, at every start (the app may have moved; Portable starts from `PORTABLE_EXECUTABLE_FILE`). `desktop/build/installer.nsh` deletes the key on uninstall (an update runs it too; the app writes it again at start).
- **host.ps1:** reads Firefox's messages (4-byte length + UTF-8 JSON), connects to the app's pipe `\\.\pipe\lamha-firefox-<hash of the data folder>`, sends the token line, then each message as a line and each answer back. With `launch: true` in a message (opening an article, Settings' Connect) it starts the app in the tray (`--hidden`) and waits up to ~20 s; otherwise it answers `no_app`. Starts in ~0.3 s.
- **The pipe (NativeBridge):** the first line must be the token (timingSafeEqual) or the socket closes; then one request at a time; handlers by type (`Object.hasOwn`); answers over 1 MB refused (Firefox's limit). main.js `BRIDGE_CALLS`: `hello` → { app: "lamha", version, wiki: { langs } }, `wikiSummary` (titles checked: ≤ 6 strings ≤ 120; lang /^[a-z]{2,3}$/), `openReader`. Setting `firefoxLink` (storage.local, on by default): Settings → Firefox in the app (before Updates, which stays last) shows whether Firefox is connected.
- **Firefox (background.js):** `askApp()` over `browser.runtime.connectNative`, ids, 8 s timeout (30 s with launch), the port closed after 5 min unused; `appAllowed()` = the optional `nativeMessaging` permission and `appLink` (storage.local). `appInfo()` (hello, kept a minute) feeds `offlineWiki()` (now async), so the card's Wikipedia part works from Firefox exactly as in the app; answers go through `cleanWiki()` (strings cut to size, https Wikipedia URLs only, data:image thumbs only). A background lookup never starts the app.
- **The card's link** (*اقرأ المقالة في لمحة*) now sends `wikiOpen` in both hosts (the app's background calls `LamhaWikiOffline.open`, Firefox asks the app with launch). The `wiki/` address route in main.js is gone.
- **Firefox Settings:** section `#appPanel` (hidden unless `browser.runtime.connectNative` exists: not on Android, not in the Windows app): status, Connect (asks the permission from the click, then `appLink` → the background tries the app with launch), Try again, Disconnect (gives the permission back).
- Tests: test-desktop 3 (the files and registration, the pipe's token/order/errors, the real host.bat → PowerShell → pipe with 4-byte framing, and `no_app` when the app quits), test-writing 4 (the app's copy on the card with fields checked, not connected = never asked, Connect/not installed, open the reader), test-ui 1 (Settings: hidden without connectNative, Connect → connected with the languages, Disconnect) and the card's link; desktop self-test 1 (the real app: the host it wrote answers hello and a summary; Settings shows "Firefox متصل الآن"): 70/70.
- **Not tried yet:** a real Firefox against the real app (needs the key in the real registry: the installed app, or `npm start`, writes it). Try: install/start the app, then in Firefox Settings → لمحة لـ Windows → اتصال; look up an English word with Wi-Fi off; press اقرأ المقالة في لمحة with the app closed (it should start in the tray and open the reader).
- Flaky, not a regression: one self-test run failed the three clipboard-formatting checks (HTML kept, نسخ, the quick panel) and the next passed 70/70; the clipboard is shared with whatever else runs on the PC. Seen once before with "reads the selection from another app".
- Later over the same link: the double-click Write button (the app would know exactly when the extension handles a box), settings, the review deck and history.

## After 1.9.4: Wikipedia downloads never started (2026-09-28, released in 1.9.5)

The user's screenshot: both downloads stopped at once with "The connection dropped", and the list below said "Downloaded" for them.
- **The cause:** Kiwix's catalog rounds sizes **up to whole 512-byte blocks** (Arabic Top · Mini: listed 226,337,792, real 226,337,504). The downloader treated the listed size as exact, so every mirror looked like "another file" (`mirror`), and after two rounds it gave up (`offline`). No download could ever start; the tests' fake catalog used exact sizes.
- **The fix (wiki-library.js):** the exact size comes from the `.meta4`'s `<size>` (`meta4Size`) or the first answer (Content-Length, or Content-Range's total when resuming), accepted when within 64 KB of the listed one (`nearSize`), then `d.exact`. A real size far from it still refuses the mirror (nothing written). The SHA-256 still checks the file. Downloads saved by 1.9.4 (mirrors known, no `.meta4` read again) learn it from the answer.
- Tried against the real Kiwix: Arabic Top · Mini downloaded in ~20 s from the fastest mirror, SHA-256 matched, 231,103 articles, باريس found.
- **Settings:** the catalog says **Downloading / Paused / Not finished** for an entry still in the list above (only a file on the PC is "Downloaded"; `owned()` also redraws when a download fails). 0 bytes reads "0 MB", not "1 MB". A download row is a grid: name and progress beside the buttons, then the bar and the error across the whole row (the bar ends where the buttons end).
- Tests: test-desktop 1 (a rounded catalog size with and without the .meta4's size; a size far off writes nothing): fails on 1.9.4. `npm run shots` adds two sample download rows (stopped, paused) to Settings.
- Released quickly as 1.9.5 (Wikipedia downloads were broken in 1.9.4 for everyone), with its `shared/changelog.js` entry; `npm run smoke` passed 70/70.

## A full audit of the code after 1.9.5 (2026-09-29, released in 1.9.6)

The user asked for a full test of the app: every module read, how the parts connect, bugs found and fixed. All five test files pass (73 + 51 + 65 + 8 + 31 = 228, 7 new), the lint is clean. Each fix below has a test that fails on 1.9.5, except where noted.
1. **"constructor" crashed offline lookups** (`local-dict.js` `find`: `forms[w]` read Object's own `constructor`). The dictionary has the word (مُقاوِل بناء). Also `lookupAr`'s shard, `LamhaAI.errorInfo`'s table and the clipboard's أضف للمراجعة (`clipboard-actions.js` took "constructor" as already in the deck) now use `Object.hasOwn`.
2. **Days by the calendar** (`background.js` `dayKey`): "yesterday" was now − 24 h, which the night the clocks go forward (23 hours: Egypt, Lebanon, Europe…) turns into two days back. Just after that midnight a review reset the review streak to 1, and `countActivity` pruned yesterday's activity for good (the Today streak broke). Test: Cairo, 2026-04-25 00:30 (`atTime()` in test-writing sets TZ and the VM's clock).
3. **A cached lookup left no trace** (`lookup` returned the cache before the history): the word didn't go back to the top of the history, and after clearing the history (or in the Windows app, whose cache lives for days) it was never recorded again. The history / auto-card / milestone part is now `remember()`, run for both.
4. **Offline Wikipedia's "nothing found" was cached**, so a copy downloaded later didn't answer until a restart (`wikiSummary`).
5. **The reader's cleaner threw on a stray "%"** in a link (`sanitize.js`: `decodeURIComponent`), so one bad link kept the whole article from showing. `decode()` never throws. (On 1.9.5 the test is an unhandled rejection that stops test-ui.)
6. **The popup's quick translation** said only "Couldn't translate" for "Local only" misses and AI translator errors; `quickError()` says what the card says.
7. **The Windows Write button** (the user's open report): the helper now looks for the extension's button (a 350 ms wait, then a search of the page's last children) only in browsers (`BROWSERS`); before, it did so in every Chromium app (Slack, Teams, WhatsApp, VS Code…), where a big page could outlast the 1.5 s answer limit. A likely cause, **not confirmed**: no test (main.js has no unit harness), and a measurement in VS Code only reached the "not a text box" answer (~70–120 ms either way).
- Smaller, no test: the card's العربية ⇄ English switch no longer reloads a card that closed meanwhile (`content.js`); `wiki-library.js` `sleep()` removes its abort listener (a long download with many retries piled them up); `translation-report.md` is in `ignoreFiles`; "shortcut failed" logs the error's name only.

**Found, not changed (for the user to decide):**
- The Firefox bridge's pipe name is fixed: another *user* on the same PC could create it first and receive the token and the words looked up (named pipes are machine-wide). Rare on a home PC; Node can't ask for FILE_FLAG_FIRST_PIPE_INSTANCE.
- In the Windows app `navigator.onLine` doesn't exist in the main process, so `browserOffline()` is always false there: offline, a lookup first waits for Google to fail (fast when there's no network at all) before the dictionary or a pack answers.
- The popup's 🔊 (review, word of the day) has no system-voice fallback like the card's, so it's silent when Google's voice can't be reached.
- A card the user removed from review comes back when the word is looked up again (cardsAuto), as it always did for fresh lookups.

**Next steps:** run `cd desktop && npm run smoke` (it takes over the mouse, keyboard and clipboard, so it wasn't run here); try the Write button in Slack/Teams/WhatsApp with a development build and read its `[write button]` lines; add the fixes to `shared/changelog.js` with the next version.

## Security, backup, reports and the shared deck (2026-09-29, released in 1.9.6)

The user's list of 16 suggestions, 1–4 and 6–16 (5, code signing, is theirs to decide: Azure Trusted Signing ~$10/month would remove SmartScreen warnings and let electron-updater check the publisher). All tests pass (81 + 54 + 65 + 8 + 36 = 244), the lint is clean.
1. **Permissions:** `lockPermissions()` in main.js: pages get `clipboard-sanitized-write` only (checked in real Electron: geolocation, camera, notifications denied; Chromium asks for the clipboard by that name). Self-test check added.
2. **CSP** on popup.html, options.html (both hosts), card.html, panel.html. Checked in real Electron with hidden windows: no violations, the card draws, a control (inline script, an http image) is blocked and reported; the backup's blob download works under it.
3. **The card and the reader** may send only `OUTSIDE_TEXT_MESSAGES`: not `aiTest`/`ollamaModels` (a request to any address on the network), nor deleting or reading the deck. Self-test check added.
4. **`e.isTrusted`** on pointerup, dblclick and keyup in content.js: a page can't fake the user (instant mode sent a page-chosen selection to the translator). test-ui wraps listeners so its dispatched events count as the user's unless marked `byPage`.
6. **Fuses** `onlyLoadAppFromAsar` and `enableEmbeddedAsarIntegrityValidation` (the schema's name; `embeddedAsarIntegrityValidation` is refused). Verified on a real `electron-builder --dir` build without launching it: the fuses read back on, and the exe's embedded SHA-256 equals app.asar's header hash, so it will start. `grantFileProtocolExtraPrivileges` left on (all pages are file://; untested).
7. **The Firefox link's pipe name** is random per start (in config.json only), and host.ps1 reads config.json again at each reconnect (before, a restart of the app also changed the token and locked out a running host). Tests in test-desktop.
8. **The clipboard panel's window** is created only while clipboard history is on (or at the first Alt+Shift+V).
9. **The deck in memory** (see Storage above): one read instead of one per operation. Test with storage events (`makeEnv({ events: true })`).
10. **forms.json split** into `dict/forms/<xx>.json` (390 parts, largest 49 KB): the first English lookup 16 → 3.5 ms. build_dict.py writes this layout; the split was done with the same rule.
11. **Memory:** the installed 1.9.5 measured ~445 MB private (GPU 193, main 97, three windows 133, services 22); the self-test now reports `app.getAppMetrics()` at its end (fails over 1.5 GB).
12. **Content scripts in real Firefox:** not measured (needs Firefox on screen). How: about:performance with a page with many frames, before and after disabling Lamha.
13. **`desktop/app-rules.js`:** `changesFor`, `allowedFromOutsideText`, `wikiSummaryArgs`, `optionsTarget`, `cardPlacement`, `panelPlacement`; main.js uses them; 5 tests in test-desktop. In `package.json` files.
14. **Report a problem** (Settings → Privacy): `diagnostics()` builds text from settings, counts, the error log (`errorLog` in storage.local: the last 30 failures, codes only, quotes removed) and, in the app, `LamhaDesktopInfo()` (versions, memory, the last 10 Write-button decisions: program, what UI Automation said, why). Shown before sending; Copy, or Report on GitHub (a new issue with it, cut to ~7,000 characters for the address). Nothing is sent automatically. Test: no word, key, text or address in it.
15. **A copy of my data** (Settings → Privacy): `dataExport` / `dataImport` (merge; the journal and activity take the copy that has seen more; everything rebuilt from known fields). Tests: merging, twice changes nothing, a hostile file.
16. **The deck shared between Firefox and the app** over the link: `syncDeck()` (Firefox; after deck changes, 15 s later; at start; on Connect; `deckSyncNow`) and `answerDeckSync()` (the app, `BRIDGE_CALLS.deckSync`). Words changed since `deckSyncAt`, in pages of 300,000 characters (the link's 1 MB is bytes, and Arabic is two; `MAX_REPLY` now counts bytes, `MAX_LINE` 1.1 MB). Never starts the app. Test: two real backgrounds joined by a fake link: words, schedules and removals both ways, 3,000 cards in pages under 1 MB. Settings' Firefox section shows when the decks last met.
- Found on the way: a tombstone older than 180 days is pruned, so restoring a copy older than the deletion brings the word back (accepted).
- **Before 1.9.6 went out:** `npm run smoke` first failed 2 of 74, both in the self-test itself: the new card check looked "bank" up, which added it to the deck before the clipboard's أضف للمراجعة check (now an offline Wikipedia request, which changes nothing); and the reader check faked its mouse-up with dispatchEvent, which content.js now ignores (now a real double-click with `sendInputEvent`; screenshots.js too, and its pill picture was checked). Then **74/74** (Ollama, optional, not running). Memory after the whole self-test: ~650 MB (GPU ~330).
- **Next:** a real Firefox against the real app for the shared deck (Connect, add/review/delete on both sides); #12 in real Firefox (about:performance).

## Offline, the popup's voice, and performance (2026-09-29, released in 1.9.6)

- **The Windows app knows when there's no network:** main.js gives the background `navigator.onLine` from Electron's `net.isOnline()` (Node's navigator has none, so `browserOffline()` was always false there). The dictionary, packs and a downloaded Wikipedia then answer without trying the internet first (`wikiSummary` skips it too, and doesn't cache the copy's answer meanwhile). Self-test check added (not run yet).
- **The popup's 🔊** (review, word of the day) falls back to the system voice like the card: Google only when online and not "Local only"; a second tap stops.
- **Performance** (`npm run bench`, new): measured first, then fixed.
  - A network that's connected but answers nothing held a dictionary word with its sentence (the default setup) for **~52 s**: 12 s timeout, a retry, the second endpoint the same. Now: a timeout isn't retried and ends the endpoint loop (`fetchJSON`, `googleJSON`; words 8 s, batches 12 s); after a network failure Google requests fail at once for 20 s (`netDownUntil`, `googleDown()`, also in `trPlan` and `speak`); and a dictionary answer waits at most 2.5 s for its sentence's translation (`within()`, `CONTEXT_WAIT_MS`; not cached then). Result: ~2.5 s, then the next words instantly with no requests.
  - The sentence's translation starts together with the lookup (`ctxAsk`): Online first with a sentence 468 → 310 ms, a word not in the dictionary 480 → 329 ms.
  - Windows app: `broadcast()` no longer sends the deck, history, journal… (`PAGE_DATA`) to the card, the clipboard panel and the reader (none read them; with 3,000 cards it was ~1 MB each per lookup, unpacked by the card just before the lookup's answer), nor anything to the clipboard monitor's hidden window.
  - content.js reads its two storage.local values in one call (every frame of every page).
  - Measured fine, left alone: a word from the dictionary 1–3 ms once its shard is read (forms.json 1.2 MB, ~15 ms once per background start); a 3,000-card deck 5–8 ms per operation; clipboard search 0.2 ms over 500 clips; a .zim summary 8 ms (English Top · Mini on this PC); content scripts 163 KB per frame.
  - Not changed, could be later: the clipboard panel's window is created at startup even when clipboard history is off (a renderer process kept for the 150 ms target); the deck is read whole from storage for every card operation (an in-memory copy would need care with Settings' "delete all cards").
- Tests: test-writing 4 (no network, dead network with a 50× clock: `makeEnv({ scale })`, the parallel sentence, navigator), test-ui 1 (the popup voice). All fail on the code before.

## Smaller sessions, and four changes for 1.9.7 (2026-10-01, version 1.9.7 bumped, not tagged yet)

- **Fewer tokens per session** (the user's question): CLAUDE.md went from 90 KB to 19 KB (this file and docs/ideas.md hold what moved out), with a code map, a test map, and rules for sessions and models (Claude can't switch models itself; it says in one line when a task suits Sonnet or Opus). The test files take a name filter and `-q` (`tools/test-args.mjs`, `npm run test:quiet`); test-ui always runs whole, because its steps build on each other (a filtered run failed where the whole file passes).
- **Settings → Updates** shows only the installed version's notes; older ones are on GitHub ("All releases on GitHub").
- **Pinned clips always on top** (`clipboard-store.js` `pinnedFirst`), in search results too; the quick panel still opens on the newest copy (`toNewest` in clip-list.js) so Alt+Shift+V then Enter pastes the last copy. Ctrl+1…9 now count pinned items first.
- **One design language** (the user's screenshot of a system dropdown list): dropdowns styled once in ui.css with a line arrow per theme (`--select-arrow`); in the Windows app the open list is Lamha's (`appearance: base-select`, Electron 44 supports it: checked in a real window); Firefox draws its own list, in our colours. Settings' three `confirm()` boxes and the clipboard's questions use `LamhaDialog` (shared/dialog.js), with the keyboard on Cancel. The search box's ✕ is ours. A test keeps `confirm`/`alert`/`prompt` out of the pages. Left as the system's: hover tooltips, the colour of selected text, the file picker, Firefox's own prompts.
- **The waiting mark** (the user's references: a book whose pages fan, and a "searching" logo animation; Pinterest only gave stills): candidates were previewed at real size and 4× (book, lens, letter swap, write, dot); the user chose book for words, lens for sentences, write for the AI. `LamhaMotion.wait(host, kind)`, shown after 250 ms (dictionary answers don't flicker), still and fading at "subtle", nothing at "off"; Arabic turns pages left → right, English mirrors. Checked in the real card in Chromium.
- Found on the way: in Electron test scripts, destroying the only window quits the app (`app.on("window-all-closed", () => {})`); a page with a language hash reloads once, so `loadFile` rejects (catch it and wait). Here-documents in Git Bash collapse `\\` to `\`: write scripts with the editor.
- `npm run smoke` first failed 4 of 74, all in the self-test itself: the cap check expected the pinned clip last, and three checks looked for the clipboard's old `.lc-dialog` (now `dialog.dlg`, shared/dialog.js). Then **74/74** (Ollama, optional, not running). Memory after the whole self-test: ~615 MB.

## After 1.9.7: optimization, measured first (2026-10-01, not released)

The user asked what dropping Firefox would cost and what optimization would really gain. **Firefox:** about 5–10% of the work is Firefox-only (≈210 lines of background.js: the app link, the deck sync client, menus and shortcuts; the page translator, which the app loads but never uses; Settings' app section); the rest is shared because the app runs the extension's files. Dropping it loses Mac, Linux and Android, Mozilla's signed free hosting (the app isn't code-signed), page translation, the in-page sentence and the web Write button. Suggested: keep it, frozen (no Firefox-only features); decide with AMO's daily users vs. GitHub download counts.

**The optimization list** (from `npm run bench`):
- **The bench, fixed:** its dead-network rows sped the VM's timers up 50× and multiplied the whole time back, so the work counted 50 times ("the next word" showed 3.8 s; it takes ~2 ms). Now every background in the bench runs on `virtualClock()` (its timers, `Date` and the simulated network): when nothing is left to run it jumps to the next timer, and `felt()` reads the time from it (real time for the work, the jumps for the waiting). Rows now match the code's timeouts exactly (2,500 / 8,000 ms), and the whole bench takes about a second. Anything the background waits on must be one of the clock's timers (a real wait would be jumped over). Also: the "shards already read" row read a new shard for each word (now two rows: already read, 0.4 ms; not read yet, 2.9 ms), and the deck rows have a warm-up (500 cards showed 22 ms for compiling the code).
- **B, done:** Online first on a network that answers nothing waited the word timeout (8 s), and then the sentence's 2.5 s on top of it, before the dictionary answered. Now `lookup()` gives Google `ONLINE_WAIT_MS` (2.5 s) when the dictionary knows the word, then answers from it (`onlineLate`: not cached), and the sentence's 2.5 s counts from the lookup's start (`ctxInTime`). The bench: 10.5 → 2.5 s with a sentence.
- **C, done:** connected with no internet behind it (requests fail at once) cost ~0.8 s and 8 requests: each address was retried after 400 ms. `googleJSON` now retries a quick failure only within `BLIP_MS` (60 s) of Google's last answer (`googleOkAt`); otherwise the other address is tried and that's all. The bench: 805 → 4.5 ms, 4 requests. A 5xx with no recent answer isn't retried on the same address either (the other one is tried).
- Tests: 2 in test-writing, both fail on 1.9.6. All five files pass (83 + 54 + 65 + 8 + 36), the lint is clean.
- **A, measured (not changed yet):** `npm run memory` (desktop/scripts/memory.ps1; `LAMHA_PROFILE`, `LAMHA_NO_GPU` and `LAMHA_MEMORY_USE` in main.js work from source only; `-Use` runs scripts/memory-use.js: main window, the card for three lookups, Settings opened and closed, back to the tray). "Task Manager" = private working set, "private" = commit. Default settings:

  | State | Task Manager | private |
  |---|---|---|
  | in the tray, just started | 149–156 MB | 319–322 MB |
  | main window open | 155 | 313 |
  | after a minute of use, back in the tray | 176–179 | 354–359 |
  | …without the graphics card (`-NoGpu`) | 150 | 267 |
  | in the tray, clipboard history on | 170 | 347 |
  | in the tray, the text-around-the-word helper off | 107 | 232 |

  By process after use: **PowerShell helper 41 / 79** (the largest; `app.getAppMetrics()`, and so the self-test's report and the earlier "~445 MB", never counted it), graphics 41 / 113, main 41 / 85, the two windows (main, card) 24 / 33 and 20 / 30, network 8 / 13. Gains, biggest first: (1) the helper as a small compiled C# program instead of PowerShell (a stand-in that loads UI Automation measured 4 / 22 MB, built with Windows' own `csc.exe`): about −37 / −57 MB; it would also stop being a hidden PowerShell with a mouse hook, which antivirus programs distrust; work: the PowerShell part of uia-context.js rewritten in C#. (2) Software rendering: −26 / −88 MB after use; check the animations and the card's see-through edges first. (3) Closing the main window when it hides to the tray: ~−20 / −33 MB, reopening then reloads it. The clipboard monitor's hidden window adds no process (the +14 MB with clipboard history on is the panel's window), so it's not worth replacing.
- **The Windows app's double-click Write button is gone** (the user's call: it showed in some apps and not others, and they didn't use it). Removed: the mouse hook and the text-box question from uia-context.js, double-click.js, `native.focusedTextBox` / `doubleClickZone` / `windowRect`, `showWritePill` and its timers in main.js, the `pill` placement in app-rules.js, the card's `showWritePill` message and `closeCard`'s `notify` option in content.js, `LamhaI18n.pair`, the report's Write-button lines, the self-test's two double-click checks (and `clickFromOutside`), their tests. **Kept:** the browser extension's own double-click button (content.js `dblclick`), whose Settings row is now `ext-only`; in the app, Alt+Shift+W with nothing selected opens Write new with Insert, as before (test-ui covers it). The helper now only reads the sentence around a word: 41 → 29 MB (Task Manager), the app after use 177 → 165 MB. Needs a changelog line ("the double-click button in other programs is removed; Alt+Shift+W with nothing selected does the same").
- **A (1), done: the helper is a small C# program,** `lamha-uia.exe` (`desktop/uia-helper.cs`, a port of the PowerShell `Around()`, same JSON lines; JSON through .NET's `JavaScriptSerializer`; a window-less `winexe`, so no conhost). `scripts/build-helper.mjs` builds it with `%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (.NET Framework 4.8: every Windows 10/11 and GitHub's Windows machines) into `desktop/bin/` (git-ignored); `npm start` (scripts/run.mjs) builds it when the source is newer, `dist` / `release` always; `package.json` `extraResources` puts it next to `app.asar` (checked in a real `electron-builder --dir` build); `uia-context.js` `helperPath()` finds it there or in `bin/`, and without it there's simply no sentence. Same answers as the PowerShell version on an English and an Arabic sentence (a WPF window), first answer ~200 ms instead of ~480. Memory: 2 / 12 MB instead of 41 / 79. Tests: test-desktop builds it and asks it questions (errors by their type only, still answering after a broken line); the self-test's sentence check passed with it.
- **A (2), done: the graphics card is a setting** (Settings → المظهر → الرسم بمعالج الرسوميات, `useGpu` in storage.local, **on by default**: nothing changes for anyone until they turn it off). main.js reads it from `storage-local.json` before the app is ready (`localAtStart()`, `GPU`), `app.gpu` / `app.restart` through `lamha:call` (bridge `lamhaApp`, the app's own pages only), and Settings shows "applies at the next start" with a restart button while the switch differs from what's running. Portable restarts its own .exe (`PORTABLE_EXECUTABLE_FILE`). The problem report says whether it's on. Self-test check added.
  - Then, before 1.9.8, the user chose **off by default** (`useGpu === true` to turn it on; `LAMHA_NO_GPU` and memory.ps1's `-NoGpu` gave way to `-Gpu`). Everyone who never touched the switch draws without the graphics card after the update.
- **Memory now** (`npm run memory -- -Use`): **135 / 287 MB** with the graphics card (177 / 356 this morning), **114 / 200 MB** without it. The self-test passed 73/73 (74 − 2 double-click checks + 1 graphics card).
- Found on the way: `memory.ps1` started from another PowerShell gets `{useGpu:false}` (quotes stripped), which the app can't read, and Windows PowerShell's ConvertFrom-Json accepts; the script now refuses unquoted keys. Its cleanup collects the process tree again at the end (with `-Wait 1` the late processes were left running and held its log open).

## After 1.9.8: Automatic animations, the card, and Settings in a new order (2026-10-02, for 1.9.9)

From the user's reports after 1.9.8 went out.
- **Automatic animations were Subtle on every PC in 1.9.8.** "Automatic" meant Subtle without graphics acceleration, and 1.9.8 turned the graphics card off by default, so the waiting mark only breathed (the user: "the writing animation isn't working"; Full worked). `rules.motionHint()` (app-rules.js): only a graphics card that was asked for and doesn't work counts, besides 4 GB / 2 cores. Measured first: with the card off, the marks still run at the screen's rate (Electron in a transparent and an opaque window, the screen photographed twice). Test in test-desktop.
- **The write mark:** the 2nd and 3rd lines showed whole during their delay, then vanished (`animation-fill-mode: backwards`, both style files); in English the short last line is written from the left (`LTR_MARKS`, `markSvg(kind, ltr)`). Seen in frame-by-frame pictures (Web Animations API, paused at 8 points). Test in test-ui.
- **The card's العربية | English switch** made the card shrink to its bar and grow back (the placeholder card in between), which looked like Settings jumping behind it. `load(…, { keep: true })`: the view stays, dimmed (`aria-busy`), until the other one comes.
- **Settings → Dictionary:** English is a chip among "Explain words in their own language" (still `enDict`, which older versions read); the translation language's chip is on and disabled (`o.explainAlways`). The separate English–English switch is gone.
- **Arabic meaning chips open the word** (its English meanings, offline from `lookupAr`; they only copied before): `termChip` always `navigate()`s.
- **Settings in the order users need them** (the user chose option B): Languages (new: interface language, translation language, Arabic → English) · How it appears / Keyboard shortcuts / Excluded websites · The lookup card (new: context, explain, definitions, Wikipedia, voice, text boxes) · Writing tools · Translation (only who translates) · Word review · My mistakes · Clipboard (app) · Without internet (was Dictionary; id still `dictionary`) · Offline Wikipedia (app) · Appearance · Privacy & history · Lamha for Windows / Firefox · Updates. Every row moved unchanged (the rebuild checked each block's first line and that no id was lost); ids kept, so links still land. The Windows app now shows its own Keyboard shortcuts (main.js `commands` answers with `HOTKEYS`; `.app-only` rows, "translate the page" `ext-only`); its Firefox and Updates sections go after the hidden `#appPanel` so Updates stays last. test-ui checks the whole order and what each new section holds.

## After 1.9.9: Windows' look, review by choice, the section bar's wheel, Wikipedia (2026-10-02, not released)

- **Windows' accent colour and Mica** (Settings → المظهر, Windows app only, both off by default: `accentWindows`, `windowMica` in storage.local). Tried first: Mica and Acrylic both work in Lamha's framed and frameless windows, with the graphics card on and off; the card stays solid (its window is larger than the card, the material fills it all). `rules.accentPalette()` makes Windows' colour readable per theme (WCAG 4.5:1 for links on the cards and white text on buttons; the user's #9E6804 stays, and becomes #B58C40 in dark); `accentCss()` beats ui.css's own rules; main.js `applyLook()` inserts it in every app page after each load and follows `accent-color-changed`; the card reads `--lamha-app-*` through `.root.app` (only the app's own pages). Mica clears only the page's background (`.desktop.mica`): panels and cards keep theirs. Measured on screen: Mica's #202020 / #F3F3F3 behind the page; memory after use 112 MB with both on (115 without). Test (8 colours) in test-desktop, a self-test check.
- **Review by choice:** `cardsAuto` is off by default: a lookup adds nothing, the user picks words with 🔖 (and a removed word no longer comes back). `importHistory` follows the setting. Tests in test-writing, the self-test's flashcards check now checks this.
- **The section bar and the mouse wheel:** the row scrolls only sideways and hides its scrollbar, so a wheel (up/down only) moved the page and the last links stayed out of reach, cut by the fade. The wheel over the row now shows the next hidden link whole (left in Arabic, right in English); at the ends the page scrolls. Its own scrolling into view used a 32 px margin under a 48 px fade (`TOC_FADE`). Test in test-ui.
- **Wikipedia on the card** (the user: "it takes long to show up"). Measured: ~0.3–1 s after the card when Wikipedia answers (two requests in a row). Fixed: it started only after the whole lookup, sentence included (`prefetchWiki()` starts it as soon as the word is known; `wikiSummary` shares one request per word, `wikiPending`); a network that answers nothing cost up to 3 × 8 s on every lookup (`WIKI_WAIT_MS` 4 s, `wikiDownUntil` 20 s after a failure, a timeout or 429); the Windows app said only "node" and Wikimedia throttles that (`wikiHeaders()`: Lamha's identity); the section popped in (now through `morph`). Hit Wikimedia's limit while measuring (also with the old code), so the live comparison was stopped. Tests in test-writing.
- `npm run smoke` passed 74/74.
