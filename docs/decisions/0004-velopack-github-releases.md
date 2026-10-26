# 0004. Velopack installer and updates from GitHub Releases

**Accepted, 2026-10-24.**

## Decision

Lamha 3 is installed by a Velopack setup (per user, no admin rights) and updates itself from GitHub Releases, with delta updates. The `vpk` tool is pinned in `dotnet-tools.json`. Betas use the `beta` channel (`beta-win-x64`, `beta-win-arm64` in CI).

## Why

It's the closest thing to Lamha 2's electron-updater flow: a normal installer, GitHub as the server, no store review. Velopack is built for .NET, supports NativeAOT apps, and makes small delta packages.

## Turned down

- **MSIX / Microsoft Store:** needs a signing certificate and Store review for every release. It can be added later from the same build.
- **Squirrel.Windows, ClickOnce:** unmaintained, or don't fit an AOT app.

## Consequences

- Phase 0's installer: `Lamha-beta-Setup.exe`, 35.7 MB (the app itself is 63.7 MB unpacked).
- Without code signing, SmartScreen warns at first install. Code signing (e.g. Azure Trusted Signing) is a separate decision for the owner.
- Lamha 2's electron-updater can't install a Velopack app, so the last 2.x update downloads and runs 3.0's setup (phase 6).
- **The updates in the app** (phase 5, 2026-10-26): `UpdateManager` on a `GithubSource` (pre-releases too for a beta), made for each check and dropped after it. A downloaded version installs at Restart now or Quit (`WaitExitThenApplyUpdates`, without a window).
- **Velopack's own install at start is off** (`SetAutoApplyOnStartup(false)`): it ran in a second start of Lamha too and closed the running one without letting it save. A version left downloaded installs at the next start, after the single-instance check, so only in the one Lamha starting.
- The beta workflow downloads the last beta (`vpk download github`) before packing, so each release has a delta from it.
