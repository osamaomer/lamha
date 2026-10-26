# The design system: a plan

**Goal:** Lamha 3 should feel like part of Windows 11. Every screen is built from one design system, with **Windows' Settings app as the reference** for the window, navigation, settings, controls, icons and motion. Lamha's own character stays where it means something: the card, the accent, the Arabic type, the celebrations.

Asked for on 2026-10-26, after 3.0.0's release and before the website, with four points:
- The app's icons should match Windows' icons.
- The side pane should have a back button, as Windows' Settings does.
- The narrow (compact) window should look like Windows' own.
- Windows' Settings is always the reference.

To these, the animations and small transitions and feedback that make Lamha natural and fun to use.

## Where Lamha is today

From the code, 2026-10-26:

| | Today | Windows' Settings |
|---|---|---|
| **Icons** | Lamha 2's line icons, 33 of them drawn as paths (`Controls/LineIcon`, used in 152 places): phase 1 chose them over Windows' | Segoe Fluent Icons, Windows' own icon font |
| **Navigation** | A pane built by hand (two ListViews and a `ContentPresenter`), folded to icons below a width; no back button, no hamburger | Back and search in the title bar; the pane open, or hidden behind ☰ when narrow |
| **Pages** | Swapped with no transition | Each page comes in (Entrance), a deeper page slides in (Drill in), Back goes the other way |
| **Settings** | A home of 11 tiles in two columns, each opening its page; a breadcrumb back | One column of full-width cards; a breadcrumb title and Back |
| **Type** | 12 font sizes typed directly in the screens (11, 11.5, 12, 12.5, 13, 13.5, 14.5, 15, 16, 16.5, 18, 30) and 11 in the tokens | Windows' type ramp: 12, 14, 20, 28, 40, 68 |
| **Corners** | Lamha 2's 8 / 10 / 14 px, and 21 radii typed in the screens | 4 px for controls, 8 px for cards and overlays |
| **Colours** | Lamha 2's, through 486 uses of Lamha's styles; Windows' own brushes in 4 places | Windows' theme brushes (text, fills, card strokes) and the accent |
| **Motion** | Lamha 2's timings (180 / 110 / 200 ms) for the card, skeletons, the review card, the burst; Settings → Appearance → Animations (Automatic, Full, Subtle, Off) | Fluent motion (83 / 167 / 250 ms), page transitions, the selection indicator gliding between pages, animated icons |

What's already right and stays: Mica, the content layer, the title bar's own layout, right to left with Windows' buttons on the left in Arabic, high contrast through Windows' colours, WCAG AA text, the Animations setting, and the memory budgets.

## Principles

1. **Windows first for the frame and the controls; Lamha in the content.** The window, the pane, settings, buttons, switches, fields and dialogs look and move like Windows'. The card, the answer, Review and the celebrations are Lamha's.
2. **WinUI's own controls and styles first.** A control is restyled only through tokens, and a custom template is the last resort. Fewer templates means more native behaviour, less code, and Windows' future fixes for free.
3. **One source of truth.** Every colour, size, radius, gap, icon and timing has a name in the system. A screen never types a number or a glyph.
4. **Every screen in four ways:** Arabic and English, light and dark. High contrast and 150 % text size are checked too.
5. **Motion explains.** Each animation shows where something came from or went, or confirms an action. Settings → Animations governs all of it, and Off means nothing moves.
6. **Fun in the right places.** Celebrations, Review and the card have character. Settings and the frame are calm, as Windows' are.

## The design system: one place

The "one file" is **one document to read**, plus the code that holds the values:

| Where | What |
|---|---|
| `docs/design/system.md` | **The design system:** the rules, every token with its Windows equivalent, the icon map, the component list (each with its states and its Windows reference), the motion catalogue, spacing and layout per screen type, RTL rules. Every screen follows it, and a new component is added here first. |
| `docs/design/system.html` | The same, drawn: every component in its states, both languages and themes, with motion demos. For sign-off, like phase 1's pages. |
| `Themes/Tokens.xaml` | The values: colours, type ramp, radii, spacing, sizes |
| `Themes/Icons.xaml` | **New:** each icon's name → its Segoe Fluent glyph (and the filled one for a selected page) |
| `Themes/Controls.xaml` | Lamha's styles over WinUI's controls, from the tokens only |
| `Lamha.Core/Appearance/Motion.cs` | The timings and curves, and the levels |
| `Services/Animations.cs` | **Every** animation goes through here, so the level always applies |
| `--gallery` | **New:** a window listing every component live, for checking and screenshots (like WinUI 3 Gallery) |

It's enforced by tests (step 7), and CLAUDE.md gets the rule.

## Foundations

### Type: Windows' ramp
- **The frame, settings and controls** use Windows' ramp: Caption 12, Body 14, Body strong 14 semibold, Subtitle 20 semibold, Title 28 semibold, Title large 40 semibold, Display 68 semibold. They're WinUI's own styles (`BodyTextBlockStyle`, `TitleTextBlockStyle`…), so Windows' text size setting applies.
- **The content keeps Lamha's reading styles**, rounded onto the ramp where it doesn't hurt reading: the serif headword, the hero meaning, and Arabic's taller lines (1.7 for reading, 1.5 otherwise).
- **The 12 typed sizes go**; each becomes one of the named styles.
- **Fonts:** Segoe UI Variable for Latin; Segoe UI for Arabic, which Windows itself uses.

### Colour
- **The frame and controls** use Windows' theme brushes: `TextFillColorPrimary` / `Secondary` / `Tertiary`, `CardBackgroundFillColorDefault`, `CardStrokeColorDefault`, `SubtleFillColorSecondary` for hover, `ControlFillColorDefault` and the rest. They follow light, dark and high contrast with no work.
- **Lamha's colours stay for meaning:** the accent (links, the selected page, filled buttons), the brand gradient (the mark, celebrations), success, danger, the search mark, and the card's own grounds.
- **The accent:** Lamha's indigo, or Windows' accent when Settings → Appearance asks (as today).

### Shape, space, layers
- **Corners:** 4 px for controls (buttons, fields, chips), 8 px for cards, panels, flyouts and dialogs, and the card window keeps Windows' 8 px. Lamha 2's 10 and 14 go.
- **Spacing:** the 4 px grid stays. The gaps and paddings for each kind of screen are **measured from Windows' Settings** at 100 % (page margins, the gap between a group's cards, the gap above a group's heading, a card's padding and height) and written into `system.md` as named tokens.
- **Layers:** Mica behind the window (Mica Alt for the title bar if it reads better), the content layer under the page, cards on it, and Acrylic for the card and flyouts, as today.

### Icons: Windows' own
- **Segoe Fluent Icons**, Windows 11's icon font, everywhere: the pane, settings, buttons, the card, the clipboard panel. It comes with Windows, needs no files, and is what Windows' Settings and File Explorer draw.
- **One map:** `Glyph` keeps its names (Speak, Copy, Review…), so the 152 uses don't change. `LineIcon` becomes a `FontIcon` drawn from `Icons.xaml`. Each of the 33 is matched to a Fluent glyph in `system.md`, with a picture beside Lamha 2's to compare.
- **Selected page:** the filled glyph where Fluent has one; outline otherwise.
- **Right to left:** Back, Forward, the chevrons, Undo and Redo point the way the text goes (`MirroredWhenRightToLeft`); the rest don't turn.
- **Animated icons** where Windows animates them, from WinUI's own set: Back, ☰, Settings (the gear turns on hover), Search, the expander's chevron, Accept.
- **The app's own icon** (the mark in the title bar, the taskbar, the tray, the setup) is checked as one family at 16, 20, 24, 32 and 48 px, in both themes.

## The window: as Windows' Settings

- **WinUI's `NavigationView`** in place of the hand-built pane, with **WinUI's `TitleBar`**: the app's icon, "Lamha", **Back** and **☰**, with Windows' buttons at the end (on the left in Arabic, as today).
- **Back** goes to the page before: between the pages, into and out of a Settings category, and from a word opened from History back to History. It also responds to Alt+←, the mouse's back button and Backspace outside a text field. Hidden when there's nothing to go back to, as in Windows.
- **Two widths, exactly as Windows' Settings** (no "icons only" state: [the study](ux-study.md) found Settings has none, and the user chose to follow it):
  - Wide: the pane open with names.
  - Narrow: no pane; ☰ and 🔍 join Back in the title bar, and ☰ opens the pane over the page (the fourth screenshot).

  The width where it changes is measured from Settings in step 1.
- **Search** sits in the middle of the title bar, as Settings' "Find a setting", and becomes 🔍 when narrow.
- **The selected page** has Windows' accent pill, which glides from the old page to the new one.
- **The review count** uses WinUI's `InfoBadge` in place of the hand-made badge.
- **Pages in a `Frame`,** so they come and go with Windows' transitions (see Motion).
- **The window remembers** its size, position, and whether the pane was open.
- **Settings as Windows' Settings:**
  - The home is one column of full-width cards: icon, name, what's in it, chevron.
  - A category's page has a breadcrumb title ("Settings › Languages"), Back, and groups of cards under small headings.
  - Expanders where a setting has options under it.
  - Lamha's `SettingsCard` is reworked to Windows' measurements, or replaced by the Community Toolkit's if it publishes with NativeAOT without warnings (CLAUDE.md's rule).

## Components

Each one goes into `system.md` with its Windows reference, its states (rest, hover, pressed, focused, disabled, selected) in both themes and high contrast, and where Lamha uses it.

| Component | Becomes |
|---|---|
| Buttons | WinUI's: Accent (the one main action), Standard, Subtle (icon buttons, the card's), Hyperlink |
| Switch | WinUI's ToggleSwitch: on in the accent, as Windows', instead of Lamha 2's green (a question) |
| Fields, dropdowns, password | WinUI's, with their accent underline on focus |
| Chips and choices | `SelectorBar` / segmented choices where one of a few is picked; toggle chips stay for filters |
| Messages | `InfoBar` (information, success, warning, error) in place of Lamha's banners |
| Toast ("Copied", "Deleted · Undo") | Kept, in Windows' look: Acrylic, 8 px, Body text, rising from the foot |
| Dialogs | `ContentDialog` with Windows' layout; delete in danger red |
| Menus | `MenuFlyout` with icons, as Windows' right-click menus |
| Tips | `TeachingTip` for first-time hints ("Select a word and press Alt+Shift+L") |
| Progress | `ProgressRing` / `ProgressBar`; skeletons for answers that load |
| Lists | Windows' list items: hover fill, the selected pill, swipe-free; Lamha's rows from one template |
| Empty states | One pattern: icon, one line, one action |
| Key caps | Lamha's (Windows has none), in the tokens |
| The card | Lamha's own, with Windows' corners, Acrylic, icons and button styles |

## Motion and feedback

Fluent's timing in every window, and Lamha's character in the moments that earn it. Every animation:
- runs through `Services/Animations.cs`;
- runs on the compositor, so it stays smooth while the app is busy;
- is faster at Subtle and gone at Off. Automatic follows Windows' "Animation effects".

**Timing:** Windows' 83 ms (a quick reaction), 167 ms (most things), 250 ms (pages, bigger moves), 333 ms (celebrations). Its curves: in fast and settling softly (`0,0,0,1`), out speeding up (`1,0,1,1`). This replaces Lamha 2's 180 / 110 / 200 ms (a question).

### The frame
- **Changing page:** the new page rises and fades in (Entrance, 250 ms).
- **Into a Settings category:** it slides in from the side (Drill in). **Back** reverses whichever transition brought it.
- **The selected page's pill** glides between pages.
- **The pane** opens and closes with Windows' slide.
- **Back, ☰, Settings and Search** use Windows' animated icons.
- **The window** opens as Windows' windows do (the system's animation); nothing extra.

### The pointer
- **Hover:** the fill fades in (83 ms), on every button, card and row.
- **Press:** a darker fill. Settings' cards and the card's buttons dip slightly, then come back.
- **Focus:** Windows' two-line focus ring, shown only for the keyboard.

### Feedback: an action confirms itself
- **Copy:** the icon turns into a ✓ for 1.5 s, then back (Accept animated icon). "Copied" shows in the toast.
- **Save, pin, add to Review:**
  - the bookmark fills with a small pop;
  - a pinned copy moves to the top of the list, and the others make room (Reposition);
  - adding to Review makes the pane's count tick up.
- **Delete:** the row folds away while the others close the gap (Add / Delete transitions), then "Deleted · Undo" shows, and Undo brings the row back the same way.
- **Switches and checks:** Windows' own animations.
- **Errors:** an `InfoBar` slides in under the heading. A field with a mistake gets a short shake (Full only) and its message.

### Lamha's moments (where the fun is)
- **The card:**
  - it opens from the selection, growing slightly from 96 % as it fades in (167 ms), and closes faster (83 ms);
  - its height follows the answer smoothly;
  - the skeleton shimmers while it waits;
  - Listen's waves move while it speaks.
- **Review:**
  - the card turns over to the meaning;
  - 1 / 2 / 3 each answer with their colour and a nudge;
  - the next card slides in;
  - the day's goal ring fills;
  - the letters burst when the goal is reached;
  - the streak's number rolls to its new value.
- **Today and History:** the first items come in one after another, as today, at Full only.
- **The clipboard panel:** it rises from the taskbar's side, as Windows' clipboard (Win+V) does; an item pasted flashes before the panel closes.
- **First run:** each step slides to the next; Ready has a small celebration.
- **Writing:** a correction fades from the old word into the new one; the mistake journal's count ticks.

## Features from the study

Chosen 2026-10-26 from [the UX study](ux-study.md):
- **Mini translator, always on top:** Windows' compact overlay, a small Lamha with the box and the answer, floating over what's being read.
- **Jump list:** right-click Lamha's taskbar icon for Translate, Write, Review and Clipboard.
- **Paste and translate:** the empty Translate box offers the text on the clipboard in one click.
- **Side by side:** a paragraph switches Translate to two columns, the original and its translation.
- **Write:** **Proofread** as the main button (Ctrl+Enter), **Rewrite ▾** for Improve, Formal, Friendly, Shorter and Longer. Without an AI provider, an `InfoBar` sets it up in place, without leaving the page.
- **The answer:**
  - Listen, Copy and Save sit in a row right after the meaning;
  - one icon-button style and size;
  - the accent only on what can be pressed.

## Right to left

- The window mirrors: the pane on the right, Windows' buttons on the left, Back pointing right.
- **Lines of two languages in one item** (an Arabic meaning and its English gloss) start at the interface's side, so the item has one straight edge.
- Slides go the way the text goes: Drill in comes from the left in Arabic.
- Numbers, keys and English stay left to right inside Arabic (as `Localization` does today).
- Each screen is checked in both languages before it's done.

## Accessibility

- **Keyboard:** F6 moves between the pane and the page, Alt+← goes back, Ctrl+F finds, and tab order follows the reading order.
- **Text size:** Windows' text size (up to 225 %) applies; layouts wrap instead of clipping. Checked at 150 %.
- **High contrast:** Windows' colours only, as today, checked for every new component.
- **Targets:** at least 24 px (WCAG 2.5.8), 32 px or more in the pane and lists, as Windows'.
- **Narrator:** every icon button has a name, every page a heading, and live updates (the toast, the answer) are announced.

## Steps

0. **Done 2026-10-26:** the answers below, and **[the UX study](ux-study.md)**: Windows' Settings measured on the user's PC, WinUI's own sizes, and Lamha's card, Translate and Write compared, with their findings and the features chosen. Still to measure in step 1: Settings in light, its narrow breakpoint, and Lamha's other screens.
1. **The system on paper:** `system.md` and `system.html` (foundations, icons beside Lamha 2's, components, the motion catalogue with live demos), for your sign-off as in phase 1.
2. **Foundations in code:** the type ramp, Windows' brushes, the radii, the spacing tokens; Fluent icons through the same `Glyph` names; the typed sizes and radii replaced.
3. **The frame:** `NavigationView`, `TitleBar` with Back and ☰, the three widths, the pages in a `Frame` with transitions, the window remembered.
4. **Components:** WinUI's own where they fit (`InfoBar`, `InfoBadge`, `SelectorBar`, `TeachingTip`, `ContentDialog`); Settings in Windows' pattern; the answer, Write and the card as in *Features from the study*.
5. **Motion and feedback:** the catalogue above, through `Animations.cs`, at each level.
6. **The new features:** the mini translator, the jump list, paste and translate, side by side.
7. **Every window revisited** against the system: the main window's pages, Settings, the card, the clipboard panel, first run. Each in both languages and themes, high contrast and 150 % text; screenshots for you; memory measured against the budgets (0008, 0013, 0015).
8. **Kept that way:**
   - a test fails when a screen types a colour, a font size, a radius or a glyph, or a gap that isn't on the 4 px grid;
   - the `--gallery` window;
   - CLAUDE.md's rule ("a screen uses only the system; a new component goes into it first");
   - decision record 0020.

Released as **Lamha 3.1**, after betas. The website follows, with the new screenshots.

## Questions

**Answered 2026-10-26:**
- **Icons:** Windows' Segoe Fluent Icons everywhere.
- **The pane's icons:** in colour, as Windows' Settings. Lamha's own, drawn for both themes and shown for approval first.
- **The frame, Settings and controls** fully Windows' (corners, greys, type); Lamha's look stays in the card, Review and celebrations.
- **Settings' home:** one column of cards.
- **The switch when on:** Windows' accent.
- **Motion timing:** Windows'.
- **Search** at the top of the pane finds:
  - settings;
  - words looked up (History, Review);
  - clipboard copies;
  - and always offers "Translate …" for what's typed.

The user's direction: Lamha should feel like Windows in every way, with nothing typed in a screen. Every alignment, text size, icon size and gap gets a defined measurement, and the rules exist so that what's added later stays in order. A UX study comes before any building, with questions to the user at each step. Features that make sense along the way, and creative surprises, are welcome. The study starts with what the user uses most: **the card, Translate and Write**.

The questions as asked:

1. **Icons:** Windows' Segoe Fluent Icons everywhere, in place of Lamha 2's line icons? *Recommendation: yes.* It reverses phase 1's choice, and it's what makes Lamha look like part of Windows.
2. **The pane's icons:**
   - one colour, as most of Windows' apps (Store, Photos, Notepad);
   - or in colour, as Windows' Settings (the third screenshot), which needs drawn icons for both themes.

   *Recommendation: one colour now, coloured icons as a later idea.*
3. **How far toward Windows:** the frame, settings and controls fully Windows' (corners, greys, type), with Lamha's look kept for the card, Review, the accent and celebrations? *Recommendation: yes.*
4. **Settings' home:** one column of cards as Windows', in place of today's two columns of tiles? *Recommendation: one column.* It's the reference, and it reads better in Arabic.
5. **The switch when on:** the accent, as Windows', or Lamha 2's green? *Recommendation: the accent.*
6. **Motion timing:** Windows' (83 / 167 / 250 ms and its curves) in place of Lamha 2's? *Recommendation: Windows'.* The difference is small, but everything then moves alike.
7. **A search box** at the top of the pane, as Windows' Settings has, finding a setting or a word in History? *Recommendation: yes, in step 3, if you'd like it.*

## Ideas

- **Coloured pane icons** in Windows Settings' style, drawn for Lamha.
- **Sounds:** WinUI's interface sounds, off by default as in Windows.
- **A "What's new in 3.1" TeachingTip** the first time after the update, pointing at Back and the new pane.
