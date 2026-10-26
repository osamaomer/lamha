# 0020. One design system, with Windows' Settings as the reference

**Accepted, 2026-10-26, after 3.0.0's release. The user's calls: Lamha should feel like Windows in every way, with nothing typed in a screen. Windows' Settings is always the reference, and Lamha's look stays in the card, Review and celebrations. The system was signed off as drawn in [system.html](../design/system.html), all as recommended.**

## Decision

- **One design system:**
  - [docs/design/system.md](../design/system.md) holds the rules;
  - `Themes/Tokens.xaml`, `Themes/Icons.xaml` and `Themes/Controls.xaml` hold the values;
  - `Lamha.Core/Appearance/Motion.cs` holds the timings.

  A screen uses only what's named there. A test fails when a screen types a colour, a font size, a radius or a glyph.
- **Windows' measurements, exactly:**
  - WinUI's own sizes, and Windows' Settings measured on Windows 11 ([the UX study](../design/ux-study.md));
  - the title bar 48 with Back, and search in its middle;
  - the pane 300, rows 36, the 3 × 16 pill;
  - the page's padding 28;
  - setting cards 68 high, 4 apart, rounded 4;
  - controls 32 high, rounded 4; content cards and overlays rounded 8.

  Everything else is on the 4 px grid.
- **Two widths, as Settings:** the pane open, or hidden behind ☰. There's no "icons only" state.
- **Type:** WinUI's ramp (12, 14, 18, 20, 28, 40, 68), plus three content styles: the serif headword, the meaning, and Arabic reading lines.
- **Colour:**
  - Windows' theme brushes for text, cards, controls and dividers;
  - Lamha's colours only for meaning: the accent, success, danger, the search mark, the brand;
  - the accent in dark is Windows' way, a light indigo (#9AA1FF) with dark text;
  - switches are on in the accent.
- **Icons:**
  - Segoe Fluent Icons through Lamha's `Glyph` names;
  - the bookmark becomes the star (Fluent has none);
  - the pane has coloured icons of Lamha's own, in Settings' style.
- **Motion:**
  - Windows' timings (83, 167, 250, 333 ms) and curves, through `Animations.cs`, at the user's level;
  - page transitions, the pill gliding between pages, feedback on every action.
- **Features from the study:** the mini translator (compact overlay), the jump list, paste and translate, side by side, and Write's Proofread + Rewrite ▾.

## Why

- **Native feel comes from the details Windows uses everywhere:** its sizes, icons, corners and motion. Matching them exactly is cheaper and more convincing than approximating them.
- **WinUI's own controls and styles** bring Windows' behaviour, accessibility and future fixes for free, with fewer templates to keep.
- **One named system** keeps new screens consistent, and makes alignment checkable by a test and an overlay rather than by eye.

## Consequences

- **This replaces phase 1's choices** of Lamha 2's line icons (over Segoe Fluent Icons), Lamha 2's radii (8 / 10 / 14), its green switch, and its motion timings (180 / 110 / 200 ms). `TokensTests` stops holding the changed tokens to Lamha 2's values; Lamha 2's colours stay where they're kept.
- **The work follows [the plan](../design/system-plan.md)'s steps 2 to 8** and is released as 3.1, after betas.
- **Memory:** measured with step 3's window (NativeAOT, `measure.ps1`, against `main` built and measured the same way on the same PC the same day).
  - Windows' NavigationView keeps about 3 MB of commit after the main window closes, and its TitleBar about 1 MB: 99 / 97 MB open and closed, against `main`'s 95 / 94.
  - **The user's call (2026-10-26):** keep Windows' controls and raise 0013's "back in the tray after the main window" to **100 MB** commit, rather than a hand-built pane in Windows' look.
  - The tray measured 57 MB for `main` and this branch alike that day, above 0008's 55: that's the PC, not the design system. It stays to be looked into.
