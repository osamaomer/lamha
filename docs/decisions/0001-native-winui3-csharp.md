# 0001. A native app: WinUI 3 in C# / .NET 10

**Accepted, 2026-10-24.**

## Decision

Lamha 3 is a native Windows app built with WinUI 3 on the Windows App SDK, written in C# on .NET 10 (LTS), published with NativeAOT. It replaces Lamha 2's Electron app.

## Why

- **Memory.** Lamha 2 measured ~150–165 MB in Task Manager and ~320 MB committed, most of it Chromium. Phase 0's prototype measured 29 MB in the tray and 46 MB committed.
- **A Windows 11 look and feel** (Mica, Fluent controls and motion, right-to-left, Narrator, high contrast) without imitating it in CSS.
- **Direct access to Windows** (Win32, UI Automation) instead of koffi, a separate helper exe and a PowerShell bridge.
- **A codebase organised in layers** (Core, Data, Windows, App) that can be tested without a window.

## Turned down

- **C++/WinRT:** much slower to write and review, for little memory gain over NativeAOT C#.
- **Keeping Electron:** the memory floor is Chromium's and can't be lowered much further.
- **A native shell around the existing JavaScript (WebView2):** WebView2 is Chromium too, so most of the memory saving would be lost.

## Consequences

- The logic in `legacy/background.js` is ported to C# behaviour by behaviour, with Lamha 2's tests as the specification.
- Every library must be trim- and AOT-safe (`IsAotCompatible` in `Directory.Build.props`).
- Building the ARM64 version needs Visual Studio's C++ ARM64 build tools (CI has them).
