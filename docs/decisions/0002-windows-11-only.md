# 0002. Windows 11 only; no Firefox extension, no Linux

**Accepted, 2026-10-24.**

## Decision

Lamha 3 runs on Windows 11 (22H2, build 22621, and later), x64 and ARM64. The Firefox extension is dropped, and so is the Linux plan in `docs/ideas.md` (kept on `release/2.x`).

## Why

The owner's choice: focus on one platform and make it excellent, especially its UI and UX. Windows 11 alone gives rounded corners, Mica and the newer shell features everywhere, and halves the testing.

## Turned down

- **Windows 10 as well:** loses Mica and rounded corners, doubles testing, and Windows 10 is out of support.
- **Keeping the extension:** two products with one codebase is what held the architecture to plain JavaScript in the first place.

## Consequences

- Page translation, the double-click Write button, per-site switches and the Firefox ↔ app link (native messaging, deck sync) go.
- The extension gets a last version that says it has ended and points to the Windows app (the owner's call when the time comes).
- `TargetPlatformMinVersion` is 10.0.22621.0.
