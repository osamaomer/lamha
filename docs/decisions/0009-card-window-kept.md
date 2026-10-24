# 0009. The card window is kept hidden between lookups

**Accepted, 2026-10-24, from phase 0's measurements.**

## Decision

The card's window is created at the first lookup and then hidden, not closed, between lookups (`CardLifetime.Keep`, the default). `--card=recreate` remains for measuring.

## Why

Measured with the Debug build: keeping it costs 59 MB after use; closing it costs 58 MB. Closing frees almost nothing, because what the first card costs is the XAML stack starting up, and that stays loaded. Keeping the window makes later lookups instant, and the idle trim (0008) hands its memory back to Windows anyway.

## Consequences

- Alt+F4 on the card hides it.
- The card's view model is reloaded on every lookup, never recreated.
