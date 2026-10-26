# 0016. Listen plays through Windows' wave player, not Media Foundation

**Accepted, 2026-10-26, from phase 5's speech. Replaces the plan's `MediaPlayer` for Google's voice.**

## Decision

Both of Listen's voices play through Windows' **wave player** (`waveOut` through the wave mapper, `Lamha.Windows/Media/WavePlayer`):

- **Google's voice:** each piece (`translate_tts`, at most ~190 characters) is downloaded by Lamha's own HttpClient (Core's `GoogleTranslate.SpeechPieceAsync`, 8 s like a word lookup), the next while one plays. The MP3 is described by its first frame (Core's `WaveSound.FromMp3`: Google sends MPEG-2 Layer III, 24 kHz, mono) and Windows' own MP3 codec (`l3codecp.acm`, in every Windows) decodes it.
- **Windows' voice** (offline, "Offline only", or Google's first piece not had or not played): `SpeechSynthesizer` makes a WAV, read by `WaveSound.FromWave`, and played the same way.

Lamha 2's rules are kept: Google's voice first, Windows' voice when it can't, a second press stops, nothing left playing in two places.

## Why

Measured on the published build (NativeAOT), the card closed after two Listen presses:

| | Commit after | Loaded and kept |
|---|---|---|
| Two presses of Copy (no speech: the cost of pressing buttons through UI Automation) | 103 MB | |
| Wave player | 104–106 MB | the wave mapper, its codecs, Windows' audio client (17 DLLs) |
| Media Foundation's `MediaPlayer` | 108 MB | its media engine, and a network and sign-in stack of its own: WinINet, NTLM, the AppX codec lookup (41 DLLs) |

A bare process playing one MP3 through the wave mapper grows 0.7 MB. `MediaPlayer` also downloaded Google's voice with its own network stack, outside Lamha's timeouts and cooldown.

## Turned down

- **`MediaPlayer` (the plan's choice):** a few MB more that stay for as long as Lamha runs, and twice the DLLs, to play a one-second MP3.
- **`PlaySound` from memory:** the same codecs, but a synchronous sound can't be stopped from another thread (a second press), and an asynchronous one never says when it has finished.

## Consequences

- An audio format Windows' codecs can't decode would need Media Foundation after all; Google has sent MP3 for years.
- Settings → Speak automatically and every Listen button go through `Services/Speech`.
