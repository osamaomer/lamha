# Lamha's design system

**The one place every screen follows.** Windows' Settings is the reference. Its sizes are WinUI's own, measured on Windows 11 in [the UX study](ux-study.md). Lamha's character stays where it means something: the card's answer, Review, the accent and the celebrations.

Status: **signed off by the user, 2026-10-26** ([the plan](system-plan.md), step 1; [decision 0020](../decisions/0020-the-design-system.md)). Drawn in `system.html` ([online](https://claude.ai/artifact/SfLssJ2QPaXQwjPCHEGfYw)).
- **Decided at sign-off:**
  - the bookmark is the star (filled once the word is in Review);
  - the six coloured pane icons as drawn;
  - the accent in dark the Windows way;
  - the new wording as drawn, with the user's Arabic rewordings.
- From step 2 the values live in `Themes/`, and this page is kept in step with them.

**How to use it:**
- A screen uses only what's named here: a style, a token, an icon name, a motion. It never types a colour, a size, a radius, a gap or a glyph. A test fails when it does (step 8).
- Something new is added here first, with its Windows reference, its measurements and its states, then built.
- Where Windows has a measurement, Lamha takes it exactly, even off the 4 px grid. Everything else is on the 4 px grid.

All sizes are in design pixels (what WinUI calls effective pixels); Windows scales them with the display.

## 1. The window

### Title bar: `TitleBar`
| Part | Measurement |
|---|---|
| Height | **48** (`LamhaTitleBarHeight`) |
| Back | 40 × 36, at the very start; dimmed when there's nowhere to go back to; Alt+←, the mouse's back button, and Backspace outside a text field |
| ☰ (narrow only) | 40 × 36, after Back |
| App icon | 16, then "Lamha" (Caption, 12) |
| Search | centred in the window, **512** wide at most, 32 high; **🔍 button** (40 × 36) when narrow |
| Windows' buttons | at the end; on the left in Arabic |

### Pane: `NavigationView`, left, two widths
| Part | Measurement |
|---|---|
| Open | **300** wide (`LamhaPaneWidth`) |
| Hidden | when the window is narrower than Settings' breakpoint (measured in step 2); ☰ opens it over the page |
| Row | **36** high, 40 apart, starting 18 from the window's edge |
| Coloured icon | **20**, 11 into the row |
| Name | Body (14), 63 from the window's edge |
| Selected | Windows' fill, plus the **3 × 16 pill** in the accent, rounded 2, gliding from the old row to the new |
| Count (Review) | `InfoBadge`: 16 high, Caption-size number, the accent |
| Order | Translate, Write, Review, History, Clipboard; Settings at the foot |

### Page
| Part | Measurement |
|---|---|
| Ground | Windows' content layer over Mica, its top start corner rounded 8 |
| Side padding | **28** both sides, at every width (`LamhaPagePadding`) |
| Width | fills the window; content stops at 1000 (to be measured on a wide window) |
| Title | Title (28 semibold); baseline 37 under the title bar (27 when narrow) |
| Sub-page title | the breadcrumb: "Settings › Languages", the parent in the secondary colour and a link |
| Title → first group heading | 42 between baselines |

## 2. Type

WinUI's ramp. The frame, Settings and controls use only these styles:

| Style | Size / line | Weight | For |
|---|---|---|---|
| `CaptionTextBlockStyle` | 12 / 16 | Regular | hints, the key caps, small labels, the title bar's name |
| `BodyTextBlockStyle` | 14 / 20 | Regular | everything by default |
| `BodyStrongTextBlockStyle` | 14 / 20 | Semibold | group headings, a card's name, emphasis |
| `BodyLargeTextBlockStyle` | 18 / 24 | Regular | a sentence's translation |
| `SubtitleTextBlockStyle` | 20 / 28 | Semibold | a section's title inside a page |
| `TitleTextBlockStyle` | 28 / 36 | Semibold | the page's title |
| `TitleLargeTextBlockStyle` | 40 / 52 | Semibold | first run's welcome |
| `DisplayTextBlockStyle` | 68 / 92 | Semibold | celebrations (the day's goal) |

**Lamha's content styles**, built on the ramp's sizes:
| Style | Is | For |
|---|---|---|
| `LamhaHeadwordTextStyle` | 28, the serif (Palatino Linotype), bold | the English word looked up |
| `LamhaMeaningTextStyle` | 20, bold | the meaning in context (Arabic) |
| `LamhaReadingTextStyle` | 14, line 24 | running Arabic: glosses, Wikipedia, the written text |

**Rules:**
- **Fonts:** Segoe UI Variable for Latin, Segoe UI for Arabic (Windows' own fallback), and the serif only for the headword.
- **Arabic lines** are taller: 1.7 × the size for reading, 1.5 for the rest. They're set by the style, never by hand.
- **Colours of text:** `TextFillColorPrimaryBrush` (the main), `Secondary` (descriptions, glosses), `Tertiary` (hints, placeholders), `Disabled`. Text is never in the accent unless it can be pressed.
- **Lines of two languages in one item** start at the interface's side.

## 3. Colour

**The frame and controls take Windows' brushes,** so light, dark and high contrast follow by themselves:

| Use | Windows' brush |
|---|---|
| Text | `TextFillColorPrimary` / `Secondary` / `Tertiary` / `Disabled` |
| A card's ground and edge | `CardBackgroundFillColorDefault`, `CardStrokeColorDefault` |
| A row or button under the pointer | `SubtleFillColorSecondary`, then `Tertiary` when pressed |
| Controls | `ControlFillColorDefault` / `Secondary` / `Tertiary`, `ControlStrokeColorDefault` |
| Dividers | `DividerStrokeColorDefault` |
| The page | `LayerFillColorDefault` on Mica |
| Flyouts, the card | Acrylic: `AcrylicBackgroundFillColorDefault` |

**Lamha's colours carry meaning only:**

| Token | Light | Dark | Means |
|---|---|---|---|
| `LamhaAccentBrush` | #4F46E5 | #9AA1FF | can be pressed (links, the selected page's pill, focus) |
| `LamhaAccentFillBrush` | #4F46E5, white text | #9AA1FF, text #0B0B12 (Windows' way in dark: a light accent, dark text) | the one main button, a switch when on, the count |
| `LamhaOkBrush` | #166534 | #4ADE80 | it worked |
| `LamhaDangerBrush` | #C81E1E | #FC8888 | it deletes or failed |
| `LamhaMarkBrush` | amber 30 % | amber 32 % | what a search matched |
| `LamhaBrandBrush` | #6366F1 → #06B6D4 | the same | the mark, celebrations |

- **The accent** is Lamha's indigo, or Windows' accent when Settings → Appearance asks; `AccentService` repaints the two accent tokens.
- **Warnings and errors** use `InfoBar`'s own colours.
- **High contrast** uses Windows' contrast colours only (as today); every edge is drawn in the text colour.

## 4. Shape, space and layers

| Token | Value | For |
|---|---|---|
| `ControlCornerRadius` (Windows') | **4** | buttons, fields, chips, setting cards, rows |
| `OverlayCornerRadius` (Windows') | **8** | content cards, flyouts, dialogs, the card window, the toast |
| `LamhaSpace1` … `LamhaSpace10` | 4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 | every other gap, on the 4 px grid |
| `LamhaCardPadding` | **16** | setting cards |
| `LamhaContentCardPadding` | **24** | content cards (the answer, the journal) |
| `LamhaCardGap` | **4** | between cards of one group |
| `LamhaGroupGap` | **28** | above a group's heading, after the last card |

**Layers, from the back:**
1. Mica (the window), or Mica Alt under the title bar if it reads better.
2. The content layer (the page).
3. Cards: setting cards, content cards. **Never a card inside a card.**
4. Acrylic: flyouts, menus, the card window, the toast.

## 5. Icons

**Segoe Fluent Icons,** Windows' icon font (`SymbolThemeFontFamily`), through Lamha's names (`Controls/FluentIcon.cs` holds the map; `Controls/BrandMark.cs` the mark). A screen asks for `Glyph.Copy` at a `Size` (Small, Normal, Large, Hero), never for a code point or a number.

| Size | For |
|---|---|
| **12** (Small) | chevrons, the small × in a field |
| **16** (Normal) | buttons, rows, menus |
| **20** (Large) | setting cards, the pane's coloured icons |
| **40** (Hero) | an empty state, first run's big moments |

The 33 names, mapped (seen side by side with Lamha 2's in `system.html`):

| Name | Glyph | Windows' name |
|---|---|---|
| Speak, SpeakSmall | E767 | Volume |
| Copy | E8C8 | Copy |
| Close | E711 | Cancel |
| Settings | E713 | Settings |
| Back / Forward | E72B / E72A | Back / Forward (turn in Arabic) |
| External | E8A7 | OpenInNewWindow |
| Search | E721 | Search |
| Translate | F2B7 | LocaleLanguage |
| Check | E73E | CheckMark |
| Retry | E72C | Refresh |
| Book | E82D | Dictionary |
| Sparkle | F4A5 | Sparkle |
| Bookmark | E734, filled E735 | FavoriteStar (Fluent has no bookmark; decided at sign-off) |
| Pin | E718, filled E842 | Pin / Pinned |
| Write | E70F | Edit |
| Review | E7C4 | TaskView (two cards) |
| History | E81C | History |
| Clipboard | E77F | Paste |
| Trash | E74D | Delete |
| Pause | E769 | Pause |
| Card | E737 | Frame |
| Keyboard | E765 | KeyboardClassic |
| Globe | E774 | Globe |
| Palette | E790 | Color |
| Shield | EA18 | Shield |
| Info | E946 | Info |
| Folder | E8B7 | Folder |
| Down | E70D | ChevronDown |
| List | E8FD | BulletedList |
| Text | E8D2 | Font |
| Chevron | E76C | ChevronRight (turns in Arabic): a breadcrumb's step, a card that opens a page |
| Journal | E7BE | Education: the mistake journal (system.html's) |

Added in step 4, for setting cards the names above didn't cover (each setting card has its icon, as in Windows' Settings):

| Name | Glyph | Windows' name: for |
|---|---|---|
| Swap | E8AB | Switch: Arabic text translated to English |
| Currency | E8EF | Calculator: prices in your currency |
| Timer | E916 | Stopwatch: how long copies are kept |
| PaymentCard | E8C7 | PaymentCard: bank card numbers skipped |
| Apps | E71D | AllApps: programs left out |
| SaveCopy | EA35 | SaveCopy: a copy of my data |
| Bug | EBE8 | Bug: report a problem |
| Update | E895 | Sync: automatic updates |
| Power | E7E8 | PowerButton: start with Windows |
| Calendar | E787 | Calendar: new words per day |
| Goal | F272 | Target: the day's goal |
| Download | E896 | Download: dictionaries to download |
| Motion | E945 | LightningBolt: animations (system.html's) |
| Brush | E771 | Personalize: the accent colour from Windows |
| Picture | E8B9 | Picture: the wallpaper behind the window |

Added in step 6, for the mini translator, as Windows' Calculator keeps itself on top and goes back:

| Name | Glyph | Windows' name: for |
|---|---|---|
| Mini | EE49 | MiniContract2: the mini translator, always on top (Translate's button) |
| FullView | EE47 | MiniExpand2: back to Lamha's window (the mini translator's title bar) |

**The pane's coloured icons** are Lamha's own (drafts in `system.html`), in Windows Settings' style: 20 × 20, one main colour each, soft shading, the same in both themes with an edge that keeps them clear on dark.

**Animated icons** (WinUI's): Back, ☰, Settings, Search, the expander's chevron, Accept (Copy's ✓).

## 6. Components

The default is WinUI's own control and style. Lamha's styles only set tokens.

| Component | Measurements and rules |
|---|---|
| **Accent button** | 32 high, padding 11,5,11,6, radius 4, the accent fill. **One per view**: the main action |
| **Button** | the same, Windows' control fill |
| **Subtle button** | the same, transparent until hovered: toolbars, the card |
| **Icon button** | **32 × 32**, a 16 icon, subtle; a tooltip with its name and shortcut |
| **Hyperlink button** | the accent text, no fill |
| **Split / drop-down button** | as the button, with a 12 chevron: Write's **Rewrite ▾** |
| **Field** | 32 high (multi-line grows), padding 10,5,6,6, radius 4; focused: **a 2 px accent line under it** |
| **Search box** | `AutoSuggestBox`, 32 high, 🔍 at its end |
| **Dropdown** | `ComboBox`, 32 high, radius 4 |
| **Switch** | `ToggleSwitch`, on in the accent; its "On / Off" at the start |
| **Setting card** | **68** high at least, padding 16, icon 20, 20 to the text, name (Body) over description (Caption, secondary); its control at the end, then a 12 chevron when it opens a page. **Wraps** below 476 (WinUI's threshold): the control moves under the text. What belongs under the text (a status, a list, a preview) is its `Detail`, 12 below, where the text starts (`Controls/SettingsCard.cs`) |
| **Expander** | 48 high at least; a setting with options under it |
| **Group** | a heading (Body strong), 11 from its baseline to the first card; cards 4 apart; 28 before the next heading |
| **Content card** | radius 8, padding 24, `CardBackgroundFillColorDefault` and its edge |
| **List row** | 40 high at least, radius 4, hover fill; the selected row's pill as the pane's |
| **InfoBar** | 48 high at least, padding 16: information, success, warning, error; at the top of what it's about |
| **Toast** | Acrylic, radius 8, Body, rising from the page's foot 16 above it; 4 s, or until its action is used |
| **Dialog** | `ContentDialog`, 320 to 548 wide; the main action is the accent button; Delete in the danger colour |
| **Menu** | `MenuFlyout`: 16 icons, shortcuts at the end |
| **Teaching tip** | `TeachingTip` for a first-time hint, pointing at its control |
| **Progress** | `ProgressRing` (16 inline, 32 for a page), `ProgressBar`; skeletons for an answer that loads |
| **Key cap** | Caption, padding 4,1, radius 4, the control fill and its edge (`Controls/KeyCaps.cs`) |
| **Empty state** | a 40 icon in the tertiary colour, one line (Body), one button (`Controls/EmptyState.cs`) |
| **Side by side** | a content card in two equal columns, a 1 px divider from top to bottom; each column headed by its language (Caption strong, secondary) with Listen (and Copy for the translation) where its text starts; a sentence beside its translation (reading lines, 24 high), their text 24 from the edges; 12 after a paragraph; the row under the pointer marked on both sides. From 560 wide, otherwise the answer as usual (`Views/SideBySideView.xaml`) |
| **Suggestion chip** | a pill 32 high, padding 12,0, the accent's soft tint and the accent's text, a 16 icon 8 from its words; as wide as its words, or to the far edge when they're cut with "…" (`LamhaSuggestionChipStyle`): Paste and translate |

**Targets:** nothing pressable smaller than 24; rows and the pane 36 or more.

**Focus:** Windows' two-line focus ring, shown for the keyboard only.

## 7. Motion

Windows' timing and curves, through `Animations.cs`, at the user's level:
- **Full:** as written.
- **Subtle:** the same, a quarter faster (Lamha 2's rule), without the decorative ones.
- **Off:** nothing moves.
- **Automatic:** follows Windows' "Animation effects".

| Token | Duration | For |
|---|---|---|
| `LamhaMotionFaster` | **83 ms** | hover fills, presses, something leaving |
| `LamhaMotionFast` | **167 ms** | most things: a menu, the card, a toggle, a toast |
| `LamhaMotionNormal` | **250 ms** | a page, a list making room, a resize |
| `LamhaMotionSlow` | **333 ms** | celebrations, Review's card turning |

| Curve | Value | For |
|---|---|---|
| `LamhaEaseIn` (Windows' fast out, slow in) | 0, 0, 0, 1 | anything arriving or moving |
| `LamhaEaseOut` | 1, 0, 1, 1 | anything leaving |

**The catalogue:**

| Where | What moves |
|---|---|
| A page | rises 28 and fades in (Entrance), 250 |
| Into a Settings category | slides in from the text's end (Drill in), 250; Back reverses it |
| The pane's pill | glides to the new row, 250 |
| The pane (narrow) | slides over the page, 167 |
| Hover, press | fill fades, 83; setting cards and the card's buttons dip to 98 % when pressed |
| Copy | the icon turns into ✓ for 1.5 s (Accept), and the toast rises |
| Save to Review | the star fills with a small pop (scale 1 → 1.2 → 1, 167); the pane's count ticks up |
| Pin | the copy moves to the top, the others make room, 250 |
| Delete | the row folds, the others close the gap, 250; Undo brings it back the same way |
| Error | an InfoBar slides in, 167; a field with a mistake shakes once (Full only) |
| The card | grows from 96 % and fades in, 167; leaves in 83; its height follows the answer, 250 |
| Waiting | a skeleton's shimmer; the waiting mark only after 250 ms |
| Listen | the waves move while it speaks |
| Review | the card turns over, 333; 1 / 2 / 3 answer in their colour; the next card slides in; the goal ring fills; the letters burst; the streak's number rolls |
| Lists (Full) | the first 8 items come in one after another, 22 ms apart, rising 6 |
| The clipboard panel | rises from the taskbar's side, 167 |

## 8. Right to left

- The window mirrors: the pane on the right, Back pointing right, Windows' buttons on the left.
- **Turned icons:** Back, Forward, chevrons, Undo and Redo point the way the text goes. Nothing else turns.
- Slides go the way the text goes: Drill in comes from the left in Arabic.
- Numbers, keys and English stay left to right inside Arabic.
- Two languages in one item start at the interface's side.

## 9. Accessibility

- **Keyboard:** F6 between the pane and the page, Alt+← back, Ctrl+F search, Ctrl+E the search box; the tab order is the reading order.
- **Text size:** Windows' text size applies (up to 225 %); nothing clips, rows grow.
- **Contrast:** AA for all text (checked by `TokensTests`); high contrast uses Windows' colours only.
- **Narrator:** every icon button has a name; every page a heading; the toast and the answer are announced.

## 10. Checks before a screen is done

- **Measurements:** an overlay with Windows' Settings at the same width shows the same edges, rows and gaps.
- **Four views:** Arabic and English, light and dark; then high contrast and 150 % text.
- **Motion:** at Full, Subtle and Off.
- **Memory** against the budgets (0008, 0013, 0015).
- **The test** for typed values passes.
