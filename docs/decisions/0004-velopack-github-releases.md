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
