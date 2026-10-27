# 0013. The main window's memory: closed after use, .NET's spare memory given back, 95 MB after it for now

**Accepted, 2026-10-25, from the main window's first measurements (phase 4). Provisional: the numbers after the main window are set again once its pages are built.** **Amended 2026-10-26 by [0020](0020-the-design-system.md):** back in the tray after the main window, 100 MB commit (was 95), for Windows' navigation pane. **Replaced in part 2026-10-27 by [0021](0021-main-window-kept.md):** the main window is hidden, not closed (WinUI never frees a closed window), and 105 MB after it.

## Decision

- **The main window is closed, not hidden,** when the user closes it, and made again next time. Nothing of it stays in the tray but what WinUI keeps once a control has been used.
- **When no window has been open for 5 seconds,** Lamha first runs an aggressive .NET collection (`GC.Collect` with `GCCollectionMode.Aggressive`, compacting), then hands its idle pages back to Windows as before (0008). The collection gives back to Windows the memory .NET keeps for reuse, so commit goes down too, not only Task Manager's number.
- **Two new stages in `tools/Measure/measure.ps1`:** the main window open (60 s into `--measure`) and back in the tray after it (75 s), with the plan's budget for the open window and a provisional one after it:

| State | Measured (Task Manager / commit) | Budget |
|---|---|---|
| Main window open (its frame and side pane, no page content yet) | 18 / 86 MB | 90 / 110 MB (the plan's) |
| Back in the tray after the main window | 4 / 86 MB | **45 / 95 MB** (new, provisional) |

The card's budgets (0008) are unchanged. With the collection the card now measures 40 / 84 MB open and 1 / 82 MB closed (85 MB before it).

## Why

- After the main window closes, commit stayed at 88 MB; the collection brings it to 86. What remains is WinUI's own native memory: the styles and templates of the controls it has drawn once, which .NET's collection can't reach. That was 1 MB over 0008's 85 MB, a budget set in phase 0 from the card alone; the plan marked the main window's numbers "to confirm in phase 4".
- The main window's pages (text boxes, lists, the answer) will use more of WinUI, so what stays after it will grow. 95 MB leaves room for that until the pages exist and can be measured; then the budget is set from the measurements, by a new record.
- Lamha 2.0.1 holds about 320 MB.

## Turned down

- **Keeping 85 MB and reducing first:** the remaining memory is in WinUI, not Lamha's code; finding it would hold back the main window, and the pages would add to it anyway.
- **Measuring without failing until the main window is done:** CI and the beta workflow check the budgets; a stage that can't fail would hide a real regression in the meantime.
