# 0019. The bridge: Lamha 2.1.0 offers Lamha 3 and starts its setup; Lamha 3 removes Lamha 2

**Accepted, 2026-10-26, from phase 6's bridge update. The user's calls (2026-10-26): Lamha 2 asks before installing (not by itself, not only a link), Lamha 3 removes a Lamha 2 installed for all users with one Windows admin prompt, and someone who chooses Start fresh is offered the removal too.**

## Decision

- **Lamha 2.1.0** (on `release/2.x`, `desktop/updater.js`) looks at GitHub's latest release (the API) before electron-updater does. When it's Lamha 3 (a `v3…` tag with Velopack's setup) and the PC has Windows 11, Lamha 3 is offered instead of a 2.x update:
  - **Told:** a notification ("Lamha 3 is here"), at most once a day and none for 3 days after **Later**. The tray's update item becomes **Install Lamha 3…**, and Settings → Updates says Lamha 3 is available, with a button. All three open **the Lamha 3 window** (`desktop/renderer/lamha3/`): what moves, what isn't in Lamha 3 (Lamha in Firefox; offline Wikipedia when files were downloaded, which stay), and that Lamha 2 is removed once the data has moved, its folder kept for 30 days. Then **Install Lamha 3** and **Later**.
  - **Install:** Lamha downloads `Lamha-win-<arch>-Setup.exe` for the PC's own processor (`IsWow64Process2`, because Lamha 2 runs as x64 on an ARM PC; an ARM PC falls back to the x64 setup), with its progress in the window and the tray. The file must have exactly the release's size and GitHub's SHA-256. Then Lamha 2 quits as it always does (saving its files, letting go of its shortcuts), and at its last moment (Electron's `quit`) it starts the setup. Velopack installs Lamha 3 and starts it; first run moves the data (0018).
  - **Windows 10:** not offered. Lamha 2 keeps its own updates, and Settings → Updates says Lamha 3 is for Windows 11 only.
  - **The Portable** takes the same path: Lamha 3's setup installs it.
- **Releases, in this order:** 2.1.0 is a normal 2.x release (GitHub's Latest), so it reaches users first and stays quiet until 3.0 exists. 3.0's release, and every Latest Lamha 3 release after it, also carries 2.1.0's `latest.yml`, `Lamha-Setup-2.1.0.exe` and its blockmap. electron-updater reads `latest.yml` only from the Latest release, so a 2.0.x that never updated still finds the bridge.
- **Lamha 3's half** (`Services/Lamha2Removal`, Windows' `Shell/Lamha2Install` and `Shell/KeptShortcuts`, Data's `Lamha2Folder.ClearKeepingWikipedia`), in the released Lamha 3 only, never in a beta, since both run side by side until then:
  - **Offered on first run's Ready** when Lamha 2 is installed: a third switch, **Remove Lamha 2**. It's on after a move and off after Start fresh, whose data stays where it is. It says Windows will ask for permission when Lamha 2 was installed for all users. **Start** runs the removal after the window closes.
  - **Found** by its uninstall entry: electron-builder's key, named after the app id's GUID (`…\Uninstall\209ef46e-fba3-5a6c-954e-87ea4eaef519`), for this user (HKCU) or all users (HKLM). Velopack's own entry for Lamha 3 is "Lamha". Only a `QuietUninstallString` naming an "Uninstall Lamha.exe" that exists counts.
  - **Removed** by that quiet command (`/currentuser /S` or `/allusers /S`), through Windows' administrator prompt ("runas") for all users. Cancelling the prompt leaves Lamha 2 as it was. NSIS returns at once and goes on from the temp folder, so the end shows as the entry gone (up to 5 minutes).
  - **Lamha 3's shortcuts kept:** Lamha 2's uninstaller deletes the Start menu's and the desktop's `Lamha.lnk` whatever they point at (app-builder-lib's uninstaller.nsh), and Velopack gives Lamha 3's the same name. Each one that points into Lamha 3's folder is copied into Lamha's data folder first and put back once Lamha 2 is gone, then or at the next start.
  - **Then** Lamha 2's start-with-Windows entry (`com.artworklab.lamha`, which its uninstaller leaves) goes too.
  - **Lamha 2's folder** is emptied 30 days after the move, at a start once Lamha 2 is no longer installed. The Wikipedias stay: `wikipedia.json`, `wikipedia/`, and any folder of it holding a file that `wikipedia.json` names. The move's date is a file in Lamha's data folder (`lamha2-moved.txt`), never a setting, so a backup restored on another PC can't empty that PC's folder. Without a move (Start fresh) the folder is never touched.
  - **Trying it:** `--remove-lamha2` turns it on in a beta or a build from source. It removes the Lamha 2 installed on that PC.
- **Trying it:** `LAMHA_BRIDGE_TAG=v3.0.0-beta.N` makes Lamha 2 (from source too) look at that release instead of the latest; only a `v3` tag of this repository is accepted.

## Why

- **electron-updater can't install a Velopack app** (0004); running Lamha 3's setup is what a user would do by hand.
- **Downloaded by Lamha, not a browser:** the file has no mark of the web, so Windows' SmartScreen doesn't stop the unsigned setup. GitHub's API over HTTPS gives the size and SHA-256, which catch a damaged or cut-off download.
- **The setup starts after Lamha 2 has quit:** Lamha 3 reads Lamha 2's files, which are saved by then, and 3.0 uses the same shortcuts, which are free by then.
- **At most once a day:** a tray app runs for weeks, and the 6-hourly check would otherwise nag.
- **Lamha 3 removes Lamha 2, not the other way round:** Lamha 2 can't uninstall itself while running, and if the setup failed the user would be left with neither.
- **A switch on Ready, for Start fresh too:** 3.0 has Lamha 2's shortcuts, so the two can't run together. Whoever moved their data has said goodbye to Lamha 2, so it's on; whoever started fresh may still want it, so it's off. Ready already holds the other choices that apply at Start.
- **Lamha 2's own uninstaller,** not deleting its folder: it removes its Firefox host, its app id and its files the way its setup knows them. Its process check matches Lamha 2's install folder (through PowerShell), so Lamha 3, also `Lamha.exe`, isn't closed. Only without PowerShell would it fall back to matching by name; then the shortcuts come back at Lamha 3's next start.

## Turned down

- **A `latest.yml` in 3.0's release that points straight at Velopack's setup:** electron-updater would run it as an NSIS update, with NSIS's arguments, and no word about what changes.
- **Installing without asking:** the user's call. Some people will want to finish their work, or read what ends, first.
- **Lamha 2 exporting its data for Lamha 3:** see 0018.

## Consequences

- Lamha 3's release workflow (phase 6's release) must copy 2.1.0's `latest.yml`, setup and blockmap into each Latest release.
- No 2.x release after 3.0: once 3.0 is the Latest release, electron-updater would see only 2.1.0 there.
- The GitHub API is asked once per check (every 6 hours), well under its limit of 60 an hour for each address.
- The Firefox extension's last version (saying it has ended) is a separate step, the user's to publish.
