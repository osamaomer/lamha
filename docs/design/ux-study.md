# UX study: Lamha beside Windows' Settings

Step 0 of [the design system plan](system-plan.md), 2026-10-26.

**What was looked at:**
- **Windows' Settings** on the user's PC (Windows 11, 150 % scale, dark), at three window widths: 1100, 900 and 640 px.
- **Lamha 3.0.0,** a test copy on a test profile, starting with what the user uses most: the card, Translate and Write. Each in Arabic and English, light and dark.
- **WinUI's own sizes and timings**, from the Windows App SDK 2.3.9 package's `generic.xaml`.

**How it was measured:** every size below is in design pixels (the screen's pixels ÷ 1.5), read from where the colour changes in the screenshots, and checked against WinUI's values.

## 1. Windows' measurements: the reference

Windows' Settings uses WinUI's standard sizes everywhere; the measured values match the package's.

| What | Size | Where it comes from |
|---|---|---|
| Title bar | 48 high (32 in its compact form) | `TitleBarExpandedHeight`, measured |
| Back button | 40 × 36, at the very start of the title bar | `NavigationBackButtonWidth/Height` |
| App icon in the title bar | 16 at most | `TitleBarIconMaxHeight` |
| Search | A box in the middle of the title bar, about 514 wide at a 1100 window, 32 high; only a 🔍 button when narrow | measured |
| Pane row | 36 high, 40 apart (2 above and below), 4 in from the pane's sides | `NavigationViewItemOnLeftMinHeight`, `NavigationViewItemButtonMargin` 4,2, measured |
| Selected page's pill | 3 × 16, rounded 2, in the accent | `NavigationViewSelectionIndicator*` |
| Pane icon | 16 in WinUI; Settings' coloured icons fill about 20 | `NavigationViewItemOnLeftIconBoxHeight`, measured |
| Pane icon → name | the name starts 63 from the window's edge | measured |
| Pane width when open | the page starts 328 from the window's edge | measured |
| Page title | Title, 28 semibold; a sub-page's title is a breadcrumb ("System › Display", the parent in the secondary text colour) | `TitleTextBlockFontSize`, measured |
| Group heading | Body strong, 14 semibold; the next card starts 13 below its text | measured |
| Setting card | 68 high, 4 apart (plus their 1 px edges), rounded 8; icon 20, the text 56 in from the card's edge | measured |
| Between two groups | 61 from one group's last card to the next group's first | measured |
| Field, dropdown | 32 high; padding 10,5,6,6; a 2 px accent line under it when focused | `TextControlThemeMinHeight`, `TextControlThemePadding` |
| Button | padding 11,5,11,6; 32 high | `ButtonPadding` |
| List row | 40 high at least | `ListViewItemMinHeight` |
| Expander | 48 high at least | `ExpanderMinHeight` |
| InfoBar | 48 high at least, 16 padding | `InfoBarMinHeight` |
| Dialog | 320 to 548 wide | `ContentDialogMin/MaxWidth` |
| Corners | 4 for controls, 8 for cards and overlays | `ControlCornerRadius`, `OverlayCornerRadius` |
| Type | Caption 12, Body 14, Body strong 14 semibold, Body large 18, Subtitle 20, Title 28, Title large 40, Display 68 | `*TextBlockFontSize` |
| Motion | 83 ms (faster), 167 ms (fast), 250 ms (normal); curve 0,0,0,1 | `Control*AnimationDuration`, `ControlFastOutSlowInKeySpline` |

**Windows' behaviour, seen:**
- **There's no "icons only" pane in Settings.** At 1100 and 900 the pane is open. At 640 it's hidden: ☰ and a 🔍 button join Back in the title bar, and the page fills the window.
- **Back is always there,** at the start of the title bar, dimmed when there's nowhere to go back to.
- **A setting card wraps when narrow:** its control moves under its text (Display resolution at 900).
- **The page title is the breadcrumb:** "System › Display", where the parent is a link.

## 2. Lamha today, measured against it

| What | Lamha 3.0 | Windows |
|---|---|---|
| Back, search in the title bar | none | both |
| Pane row | 32 high, the pill 9 in from the edge | 36 high, the pill 18 in |
| Pane width | 260 | about 320 (the page from 328) |
| Pane icons | Lamha 2's line icons, 16, one colour | coloured, about 20 |
| Narrow window | folds to icons | the pane hides, ☰ in the title bar |
| Cards and panels | rounded 14, 16 apart, a panel inside a panel on Translate and Write | rounded 8, 4 apart, one level |
| Field focus | a 2 px accent ring all round | a 2 px accent line under it |
| Switch when on | Lamha 2's green | the accent |
| Font sizes | 12 typed in the screens, 11 in the tokens | 8 named sizes |

## 3. Findings and proposals

Each finding comes from the screenshots. The proposals follow the plan's principles, and the open choices are in section 5.

### The frame

- **F1. No way back.** Going into a Settings category, or from History to a word, has no Back. **Proposal:** Windows' Back, as in the plan.
- **F2. The narrow window isn't Windows'.** Lamha folds to icons; Settings hides the pane. **Proposal:** do as Settings does: the pane open, or hidden behind ☰ (a question below, as the plan had three widths).
- **F3. The pane is a little cramped.** Its rows are 32 high where Windows' are 36, and it's 260 wide where Windows' is about 320. **Proposal:** Windows' sizes; the coloured icons need the room anyway.

### Translate

- **T1. Two heavy frames.** The box sits in a framed panel, and the answer in another. Windows puts a field straight on the page and results in cards. **Proposal:**
  - the box on the page, as a large Windows field;
  - the answer as one card;
  - "Quick lookup Ctrl+Alt+L" under the box as a caption.
- **T2. The field's ring.** The 2 px accent ring all round is Lamha 2's; Windows draws a 2 px accent line under a focused field. **Proposal:** Windows' field. The keyboard's focus ring stays for Tab.
- **T3. The buttons are far from the word they act on.** The meaning's Listen and Copy sit at the other end of the box from the Arabic word: 600 px away on a wide window, stacked one over the other. **Proposal:** a row right after the meaning (Listen, Copy, then Save), as one group the eye finds at once.
- **T4. Two Listen buttons, two styles.** The headword's is round, 32, accent-tinted; the meaning's is tiny and plain. **Proposal:** one icon-button style and size (Windows' subtle button, 32 with a 16 icon). The headword's Listen can stay the accent one, as the main sound.
- **T5. The accent on words that aren't buttons.** "adjective" is in the accent colour. Windows keeps its accent for what can be clicked or is selected. **Proposal:** part of speech as a caption in the secondary colour, so the accent only ever means "you can press this".
- **T6. Two edges in one meaning (Arabic interface).** The Arabic gloss starts at the right, its English line under it at the left, so the eye jumps across the box. **Proposal:** a question below.
- **T7. Aside, not design:** the dictionary's first meaning for "reluctant" is متكاسل (lazy), where غير راغب / متردد is what it means. That's in the dictionary's data, a separate task.

### Write

- **W1. Without an AI provider, Write is a dead end.** The tools are greyed out, and the way on is a button to Settings, which leaves the page and the text. **Proposal:** set up right there: an `InfoBar` ("The writing tools need an AI provider") with **Set up**, opening the same setup as Settings → Writing tools in a dialog. Gemini's free key is a link and a paste; the text stays where it was.
- **W2. Six equal tools.** Proofread, Improve, Formal, Friendly, Shorter and Longer all look the same, so there's no first step. **Proposal:** a question below.
- **W3. "Ctrl+Enter" floats after the tools,** unattached. **Proposal:** put it on the main button's tooltip and the button itself, as Windows' menus show their shortcuts.
- **W4. The mistake journal** is a framed panel holding a switch. **Proposal:** Windows' setting cards (the switch in one, Clear history in another), under a group heading.

### The card

- **C1. Listen and Copy** are far from the meaning, as in T3, with the same fix.
- **C2. The header's small green book** doesn't say what it is until it's hovered. **Proposal:** Windows' icons and a clear tooltip; check in the design step whether it's needed at all.
- **C3. Kept:**
  - the numbered writing tools (1–9 from the keyboard);
  - the links to Google Translate and Cambridge in the footer;
  - Acrylic, and Windows' 8 px corners.

## 4. Ideas: features, and a few surprises

All of them native to Windows, and all fitting Lamha:

- **Mini translator, always on top.** Windows' compact overlay (as Video's picture in picture): a small Lamha floating over what you read, with the box and the answer. For reading a long article or a film's subtitles.
- **Jump list.** Right-click Lamha's taskbar icon: Translate, Write, Review, Clipboard. A Windows touch that costs nothing in memory.
- **Paste and translate.** When the clipboard holds text, the empty Translate box offers it in one click ("Translate what you copied: …").
- **Side by side for paragraphs.** A paragraph in the box switches Translate to two columns, the original and its translation, line beside line.
- **Search in the title bar,** as decided: settings, your words, your copies, and "Translate …".
- **Copy that confirms itself,** the icon turning into ✓ (in the plan's motion).

## 5. Questions

**Answered 2026-10-26, all as recommended:**
- **The narrow window:** as Windows' Settings, open or hidden behind ☰.
- **Arabic and English in one meaning:** both start at the interface's side.
- **Write:** Proofread as the main button, and Rewrite ▾ for the rest.
- **All four ideas go into the plan:** the mini translator on top, the jump list, paste and translate, and side by side.

The questions as asked:

1. **The narrow window:** do exactly as Windows' Settings (the pane open, or hidden behind ☰), or keep an "icons only" middle width as well? *Recommendation: as Settings.* It's the reference, and one less state to keep aligned.
2. **Arabic and English in one meaning (T6):**
   - both lines start at the interface's side (the English line right-aligned in Arabic);
   - or each language at its own side, as now.

   *Recommendation: the interface's side.* The meaning reads as one block, and its edge is straight.
3. **Write's tools (W2):**
   - **Proofread** as the main button, with **Rewrite ▾** holding Improve, Formal, Friendly, Shorter and Longer;
   - or the six as they are, restyled.

   *Recommendation: Proofread first.* One clear first step, and Ctrl+Enter is its shortcut.
4. **Which ideas** from section 4 go into the plan?
