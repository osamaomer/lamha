# 0014. Notifications: Windows' own toasts, their clicks as Lamha's links

**Accepted, 2026-10-26. Built in phase 4 (the tray and notifications).**

## Decision

Lamha's notifications are Windows' toasts (`Windows.UI.Notifications`, part of Windows, no package), shown under an AppUserModelId Lamha writes for itself in the registry (`HKCU\Software\Classes\AppUserModelId\ArtworkLab.Lamha`: its name in the interface language and its icon, each start). A click on a notification or one of its buttons is a link of Lamha's own (`lamha:show?key=reluctant`, the `lamha` scheme in `HKCU\Software\Classes`): Windows starts Lamha with the link, and a Lamha already running gets it from the new one through the single-instance hand-over that already opens the main window (`AppInstance.RedirectActivationToAsync`). `Lamha.Windows.Shell.NotificationRegistration` writes both, builds and reads the links; Velopack's uninstall hook removes both. With `--profile`, both have names of their own (`ArtworkLab.Lamha.Profile`, `lamha-profile`), so trying something never takes over the installed Lamha's. When Windows' toasts can't be used, the tray icon's own balloon (no buttons).

## Why

- **The review notification has buttons** (Show meaning, then Forgot / Hard / Knew it: the tray design), which the tray icon's balloon (phase 3's) can't have.
- **The Windows App SDK's app notifications don't work for Lamha:** for an app without a package they rely on the SDK's Singleton package, which a self-contained app (Lamha ships the SDK inside it) doesn't install. `AppNotificationManager.Register()` fails with 0x8007007E on this PC, after writing its registry entries.
- **Links work wherever the notification is:** on screen or in the notification centre, with Lamha running or not. A toast's in-process `Activated` event works only while the toast object lives and, for an app like Lamha, not from the notification centre (Lamha 2's Electron notifications had the same gap).

## Turned down

- **Installing the Singleton package** with Lamha: an MSIX installed for the user, for one feature, with its own updates.
- **A COM activator** (`INotificationActivationCallback`, registered under `CLSID\…\LocalServer32`), what Windows documents for apps like Lamha: a COM server under NativeAOT, for what a link does with a registry key.

## Consequences

- Each click starts a short-lived Lamha.exe that hands the link over and exits (the published app starts quickly under NativeAOT).
- Windows lays the notifications out in its own display language: on an English Windows, an Arabic notification runs left to right (its Arabic text still aligns itself). The toast XML's `lang` didn't change that.
- `ToastNotifier.Setting` can't be read for an app registered this way ("element not found"), so Lamha doesn't know when the user has turned its notifications off in Windows; it shows them, and Windows drops them.
