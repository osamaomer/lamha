# 0008. Memory budgets on commit and Task Manager; idle memory handed back

**Accepted, 2026-10-24, from phase 0's measurements.**

## Decision

- `tools/Measure/measure.ps1` runs the app's `--measure` timeline and reads two numbers from outside the process: **Task Manager's "Memory"** (private working set) and **commit** (private bytes). The checks fail above either budget.
- When no window has been open for 5 seconds (after start-up, and after the card closes), Lamha hands its idle pages back to Windows (`SetProcessWorkingSetSize(-1, -1)`, `Services/IdleMemory.cs`).

## Phase 0's measurements

NativeAOT, x64, Release, the prototype card (`--measure`):

| State | Task Manager | Commit | Budget (Task Manager / commit) |
|---|---|---|---|
| In the tray | 29 MB, ~0 after the idle trim | 46 MB | 35 / 55 MB |
| Card open | 55 MB (32 after a trim earlier) | 73 MB | 70 / 85 MB |
| Card closed | 54 MB, ~0 after the idle trim | 73 MB | 45 / 85 MB |

Lamha 2.0.1, for comparison: ~150–165 MB in Task Manager, ~320 MB committed.

The card opened in 32–78 ms, including from the trimmed state.

## Why both numbers

Once idle memory is handed back, Task Manager shows almost nothing, which on its own would flatter Lamha. Commit is what Lamha actually holds, so it has a budget too.

## Turned down

- **Closing the card to save memory:** measured, it frees ~1 MB. The cost is the one-time start-up of the XAML stack, not the window (see 0009).

## Consequences

- Most of the ~27 MB the first card costs is XAML, composition and the acrylic backdrop. Phase 1 measures the backdrop choice (acrylic, Mica, solid) as part of the design.
- The budgets are raised only by a new record that says why.
