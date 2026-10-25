# لمحة · Lamha

**Select a word. Get it at a glance.**

Lamha is an Arabic-first dictionary, translator and English writing coach for Windows, inspired by the **Look Up** feature on iPhone and Mac. Select a word in any program and a small card shows what it means, in Arabic, in *this* sentence. Select a paragraph and it's translated. Select your own email and Lamha polishes it and explains, in Arabic, what you got wrong.

> **Using Lamha today?** The current version is **Lamha 2**: download it from [Releases](https://github.com/osamaomer/lamha/releases/latest). Its full guide is in **[legacy/README.md](legacy/README.md)**.

## Lamha 3: the native Windows app (in development)

Lamha 3 is a rewrite of the Windows app as a native Windows 11 app: lighter, faster, and at home next to the other apps on your PC. It keeps everything the Windows app does today, and adds OpenAI and custom OpenAI-compatible servers as AI providers.

| | Lamha 2 | Lamha 3 |
|---|---|---|
| Built with | Electron (Chromium + Node) | WinUI 3, .NET 10, NativeAOT |
| Memory in the tray | ~150 MB (Task Manager) · ~320 MB committed | ~29 MB · 46 MB committed (first prototype) |
| Installer | Electron + NSIS | 36 MB (Velopack) |
| Runs on | Windows 10 and 11 | Windows 11 |
| Firefox extension | Yes | No: Lamha 3 is for Windows only |

- **The plan:** [docs/native-plan.md](docs/native-plan.md): phases, the design approach, ideas and open questions.
- **Decisions:** [docs/decisions/](docs/decisions/).
- **Status:** phase 0 (foundations) is done. Next: the design system and screen designs, and the port of Lamha's brain.

## For developers

You need Windows 11, the .NET 10 SDK, and Visual Studio 2022 or later with *Desktop development with C++* (for NativeAOT; add the C++ ARM64 build tools for ARM64).

```powershell
dotnet build Lamha.slnx
dotnet test --solution Lamha.slnx
dotnet run --project src/Lamha.App          # tray icon; Ctrl+Alt+L on selected text opens the card
```

| Folder | What's in it |
|---|---|
| `src/Lamha.Core` | The logic, with no Windows dependency |
| `src/Lamha.Windows` | Win32: shortcuts, the selection, the clipboard, the tray |
| `src/Lamha.App` | The WinUI 3 app |
| `tests/` | Tests for each project |
| `tools/` | The memory check, and later the dictionary builder |
| `legacy/` | Lamha 2 (Electron and the Firefox extension), kept as the reference for the port |
| `site/` | The download page |

## Privacy and data sources

See [legacy/README.md](legacy/README.md#-privacy-and-data-sources): Lamha 3 follows the same rules.

## License

MIT. See [LICENSE](LICENSE).
