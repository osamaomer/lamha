# 0019. The bridge: Lamha 2.1.0 offers Lamha 3 and starts its setup; Lamha 3 removes Lamha 2

**Accepted, 2026-10-26, from phase 6's bridge update. The user's calls (2026-10-26): Lamha 2 asks before installing (not by itself, not only a link), and Lamha 3 removes a Lamha 2 installed for all users with one Windows admin prompt.**

## Decision

- **Lamha 2.1.0** (on `release/2.x`, `desktop/updater.js`) looks at GitHub's latest release (the API) before electron-updater does. When it's Lamha 3 (a `v3…` tag with Velopack's setup) and the PC has Windows 11, Lamha 3 is offered instead of a 2.x update:
  - **Told:** a notification ("Lamha 3 is here"), at most once a day and none for 3 days after **Later**. The tray's update item becomes **Install Lamha 3…**, and Settings → Updates says Lamha 3 is available, with a button. All three open **the Lamha 3 window** (`desktop/renderer/lamha3/`): what moves, what isn't in Lamha 3 (Lamha in Firefox; offline Wikipedia when files were downloaded, which stay), and that Lamha 2 is removed once the data has moved, its folder kept for 30 days. Then **Install Lamha 3** and **Later**.
  - **Install:** Lamha downloads `Lamha-win-<arch>-Setup.exe` for the PC's own processor (`IsWow64Process2`, because Lamha 2 runs as x64 on an ARM PC; an ARM PC falls back to the x64 setup), with its progress in the window and the tray. The file must have exactly the release's size and GitHub's SHA-256. Then Lamha 2 quits as it always does (saving its files, letting go of its shortcuts), and at its last moment (Electron's `quit`) it starts the setup. Velopack installs Lamha 3 and starts it; first run moves the data (0018).
  - **Windows 10:** not offered. Lamha 2 keeps its own updates, and Settings → Updates says Lamha 3 is for Windows 11 only.
  - **The Portable** takes the same path: Lamha 3's setup installs it.
- **Releases, in this order:** 2.1.0 is a normal 2.x release (GitHub's Latest), so it reaches users first and stays quiet until 3.0 exists. 3.0's release, and every Latest Lamha 3 release after it, also carries 2.1.0's `latest.yml`, `Lamha-Setup-2.1.0.exe` and its blockmap. electron-updater reads `latest.yml` only from the Latest release, so a 2.0.x that never updated still finds the bridge.
- **Lamha 3's half** (built in the released 3.0, never in a beta, since both run side by side until then): after a successful move, Lamha 3 removes Lamha 2. It runs Lamha 2's uninstaller quietly, found by its uninstall entry ("Lamha 2.x.y", Artwork Lab; Velopack's own entry is "Lamha"). Windows asks for an administrator once when Lamha 2 was installed for all users. Lamha 3 also removes Lamha 2's start-with-Windows entry (`com.artworklab.lamha`), and deletes `%APPDATA%\Lamha` 30 days later, keeping `wikipedia/` and `wikipedia.json`.
- **Trying it:** `LAMHA_BRIDGE_TAG=v3.0.0-beta.N` makes Lamha 2 (from source too) look at that release instead of the latest; only a `v3` tag of this repository is accepted.

## Why

- **electron-updater can't install a Velopack app** (0004); running Lamha 3's setup is what a user would do by hand.
- **Downloaded by Lamha, not a browser:** the file has no mark of the web, so Windows' SmartScreen doesn't stop the unsigned setup. GitHub's API over HTTPS gives the size and SHA-256, which catch a damaged or cut-off download.
- **The setup starts after Lamha 2 has quit:** Lamha 3 reads Lamha 2's files, which are saved by then, and 3.0 uses the same shortcuts, which are free by then.
- **At most once a day:** a tray app runs for weeks, and the 6-hourly check would otherwise nag.
- **Lamha 3 removes Lamha 2, not the other way round:** Lamha 2 can't uninstall itself while running, and if the setup failed the user would be left with neither.

## Turned down

- **A `latest.yml` in 3.0's release that points straight at Velopack's setup:** electron-updater would run it as an NSIS update, with NSIS's arguments, and no word about what changes.
- **Installing without asking:** the user's call. Some people will want to finish their work, or read what ends, first.
- **Lamha 2 exporting its data for Lamha 3:** see 0018.

## Consequences

- Lamha 3's release workflow (phase 6's release) must copy 2.1.0's `latest.yml`, setup and blockmap into each Latest release.
- No 2.x release after 3.0: once 3.0 is the Latest release, electron-updater would see only 2.1.0 there.
- The GitHub API is asked once per check (every 6 hours), well under its limit of 60 an hour for each address.
- The Firefox extension's last version (saying it has ended) is a separate step, the user's to publish.
