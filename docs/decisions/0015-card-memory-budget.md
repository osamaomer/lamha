# 0015. The card's commit budget: 95 MB, set from what the memory is

**Accepted, 2026-10-26, from phase 4's measurements. Replaces 0008's 85 MB for the card open and closed; its Task Manager budgets are unchanged.**

## Decision

The card's commit budget is **95 MB** open and closed (it was 85, set in phase 0 from the prototype card). Task Manager's stay at 70 / 45 MB. The other stages are unchanged: the tray 35 / 55, the main window 90 / 110 open and 45 / 95 after it (0013).

## What the card's memory is

Measured on the published build (NativeAOT, x64) on the development PC, 2026-10-26: with the card open, commit 85–86 MB in six runs, Task Manager 41 MB. From inside (`--measure` logs .NET's share) and from outside (the process's memory, region by region):

| Part | MB | What it is |
|---|---|---|
| One graphics memory pool | 27–28 | The graphics stack's own: 11.6 MB already in the tray, grown when the first window of any kind draws (the main window makes it too). 94% of its pages are never written; Direct3D's software renderer (WARP) and its shader compiler load with it. |
| WinUI's native memory | ~33 | Styles, templates, text, composition |
| Programs' data pages | ~20 | 9 MB of the graphics driver's (Intel's shader compiler, `igc64.dll`, in the tray already), 5.5 MB of Lamha.exe's own, Windows' DLLs |
| .NET's heap | 3 | Lamha's own objects |

Measured and ruled out, each changing nothing: the card's Acrylic (a solid card measures the same), lookups on the internet (offline: the same), the sentence reader (off: the same), the dictionary file (missing: the same).

## Why

- What grew since phase 0's 73 MB isn't Lamha's code holding memory: it's WinUI and the graphics stack, for a real card (phase 0's was a prototype) and an app with more of WinUI in it. Closing or simplifying the card wouldn't bring it back (0008 measured closing it: ~1 MB).
- At 85 MB the check failed on noise (85–91 MB from run to run, more after the clipboard panel has been used), so it had stopped telling a regression from a normal run.
- The graphics pool and the driver's data depend on the PC's graphics: 95 MB leaves room for a PC whose driver keeps a little more, while still catching a real regression of several MB.
- 95 MB is also what's allowed after the main window (0013): the card open shouldn't be held tighter than the tray after a window.

## Turned down

- **Keeping 85 MB:** it fails on noise and hides real regressions among the false ones.
- **Raising Task Manager's budgets too:** the card is at 41 of 70 MB there; no reason to.

## Consequences

- `tools/Measure/measure.ps1` checks 95 MB for "card open" and "card closed".
- The budgets are measured on the development PC; the betas on other PCs (Intel, NVIDIA, AMD graphics) are where a pool of another size would show.
