# 0005. No web view in 3.0

**Accepted, 2026-10-24.**

## Decision

Lamha 3.0 has no web view. Offline Wikipedia (`.zim` files) and the reader window wait for a later release. The short online Wikipedia summary on the card stays, as plain text.

## Why

WebView2 is Chromium (~100 MB while open), the biggest single memory risk. The owner asked for offline Wikipedia to come back later with a better implementation, rather than be ported as it is.

## Consequences

- 2.x users' downloaded Wikipedia files stay on disk, untouched, for when the feature returns.
- Ideas for its return (a native reader built from XAML text, or a web view in its own process, title search in SQLite, downloads through BITS) are in `docs/native-plan.md`.
