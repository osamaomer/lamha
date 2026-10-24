# 0012. The sentence around a word: UI Automation in Lamha's process, on its own thread

**Accepted, 2026-10-24. Built in phase 3.**

## Decision

The sentence around a word selected in another program (so the lookup picks the meaning that fits) is read with UI Automation from inside Lamha, through CsWin32's COM bindings (no marshalling, NativeAOT-safe): `Lamha.Windows.Selection.SentenceReader`. It runs on one background thread in the multithreaded apartment, with UI Automation's own timeouts at 1 s; the shortcut waits for it at most 700 ms (Lamha 2's number). A question still running after 3 s means a frozen program: the next lookup gets a new thread and the old one ends when its call returns. The rules are Lamha 2's `uia-helper.cs`: the focused element and its ancestors first, then documents and edit boxes below (500 ms at most), only while focus is in the program the text came from, and only when its selection is the text Lamha just copied; 400 characters on each side.

## Why

- Lamha 2 needed a helper process (`lamha-uia.exe`) because Node couldn't call UI Automation, and it kept a hung program from blocking Electron's main process. In Lamha 3 a thread does the second job: the UI thread never waits on UI Automation, only on a task with a timeout.
- No second executable to build, sign, ship and keep running (Lamha 2's helper cost ~4 MB in Task Manager while it ran).

## Turned down

- **A helper process, as in Lamha 2:** more to ship and measure, for a protection a thread and UI Automation's timeouts give.
- **`System.Windows.Automation` (UIAutomationClient):** .NET Framework / WPF only, not NativeAOT.

## Consequences

- UIAutomationCore.dll loads into Lamha the first time (`SentenceReader.Warm()` at start when the setting is on, so the first lookup doesn't pay for it). The tray and card budgets were met with it loaded (decision 0008).
- The same thread can later read the selection itself without the clipboard (the idea in the plan), when that's decided.
