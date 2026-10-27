# 0021. The main window is hidden, not closed: WinUI never frees a closed window

**Accepted, 2026-10-27, the user's call (design system step 7). Replaces [0013](0013-main-window-memory.md)'s "closed, not hidden"; its budget after the main window goes from 100 to 105 MB commit.**

## Decision

- **Windows' close hides the main window** (`AppWindow.Closing` cancelled, then `AppWindow.Hide`), and the next open shows the same window again, on the page asked for. The mini translator hides it too.
- **Hidden, it rests as a closed window did:**
  - its place is saved;
  - what the Translate and Write pages were waiting for is dropped;
  - clipboard history's decrypted copies and Settings' listeners go;
  - the page is taken out, so it comes back fresh when the window does, and Back starts again.

  IdleMemory counts it closed, so its working set goes back to Windows as before.
- **It really closes** when Lamha ends (Quit, an update's restart, Delete my data) and when first run changes the interface language, since its words are in the old one.
- **The budget after the main window is 105 MB commit** (was 100). Task Manager's 45 MB is unchanged.

## Why

- **Every open and close of the main window kept memory, in every version:**

  | Commit after each close, five opens (NativeAOT) | 1 | 2 | 3 | 4 | 5 |
  |---|---|---|---|---|---|
  | 3.0.0 (as released) | 87 | 90 | 94 | 95 | 99 |
  | Design system step 6 | 102 | 106 | 111 | 114 | 120 |
  | This decision | 102 | 102 | 102 | 102 | 102 |

  Twenty opens in a long session would have added 60–90 MB.
- **It's WinUI's, not Lamha's:**
  - .NET's heap stays at 2 MB while commit grows 19 MB, and the closed windows stay alive with no .NET reference to them;
  - a bare WinUI window doesn't leak, while each control the main window uses loses a little (the navigation pane about 1 MB, the title bar about 1.3 MB);
  - the known part didn't stop it: x:Bind in a Window subscribes to its own Activated and never unsubscribes (removed by hand, it changed nothing);
  - nor did the other usual fixes: the window's and its service's own handlers unsubscribed, its content emptied on close, a second collection after the finalizers, Mica off.
- **A newer Windows App SDK can't fix it yet:**
  - Lamha uses the newest stable releases (WinUI 2.3.9, Foundation 2.3.12, InteractiveExperiences 2.1.9);
  - the experimental ones, whose notes mention better cleanup of native WinUI objects, don't build Lamha: the XAML compiler stops with WMC9999.
- **Opening is quicker too:** 15 ms again, against 60–80 ms for a new window.

## Consequences

- `MainWindowService`:
  - `Show` shows the kept window again;
  - `Hide` is what the user's close, the mini translator and `--measure` do;
  - `End` really closes it (the app's Close, first run's language change).
- `MainWindow`:
  - closing calls `Hide` unless `End` was called;
  - `Rest` is what both closing and hiding leave behind;
  - `ShowAgain` brings it back.
- `measure.ps1`:
  - the timeline hides the main window at 75 s, as the user's close does;
  - "main window closed" is 45 / 105 MB.
- **The mini translator** has the same WinUI leak at each open; it's opened rarely, and is measured in step 7.
- **Try each new stable Windows App SDK** with five opens and closes: if a closed window is freed, the main window can close again.

## Turned down

- **Closing it as before, and accepting the leak:** the growth has no limit while Lamha runs, which can be weeks.
- **Experimental Windows App SDK packages:** they don't build Lamha, and a release can't ship them.
