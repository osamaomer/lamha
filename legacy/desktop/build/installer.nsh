; Lamha: additions to the Windows installer (electron-builder includes build/installer.nsh by itself).
; Firefox finds the app through this key (desktop/native-bridge.js, Settings -> Firefox). Uninstalling removes it, so
; Firefox no longer looks for an app that isn't there. An update runs the old uninstaller too: the app writes the key
; again when it starts.
!macro customUnInstall
  DeleteRegKey HKCU "Software\Mozilla\NativeMessagingHosts\com.artworklab.lamha"
!macroend
