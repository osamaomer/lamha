# 0003. One repository: `main` is Lamha 3, Lamha 2 in `legacy/` and on `release/2.x`

**Accepted, 2026-10-24.**

## Decision

- Lamha 3 lives in this repository, in `src/`, `tests/` and `tools/`, and `main` becomes Lamha 3.
- Lamha 2's code moves to `legacy/` on `main`. It's kept as the reference for the port and for the parity tool, and its tests still run when it changes (`.github/workflows/legacy-checks.yml`). It's deleted in phase 6.
- Lamha 2's fix releases are built from the **`release/2.x`** branch, which keeps the old layout and its own release workflow.
- The tag **`electron-final`** marks `main` just before the move.
- `dict/`'s git history is kept (rewriting published history breaks every clone), but it gets no new commits: Lamha 3 builds its dictionary in CI.

## Why

Issues, stars, releases and the download links stay where users already know them. One `git log` still tells the whole story.

## Turned down

- **A new repository:** loses issues and release history, and Lamha 2's updater looks for releases in this one.
- **Leaving Lamha 2's files at the root until the end:** the root would mix two products for months.

## Consequences

- A GitHub release published from `main` must **never be marked "latest"** before 3.0: Lamha 2's updater asks for `/releases/latest`. Beta releases are pre-releases.
- The website workflow reads `legacy/shared/changelog.js` until Lamha 3 has its own changelog.
