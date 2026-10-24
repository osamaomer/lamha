# 0010. The AI providers over Lamha's own HTTP, not their SDKs

**Accepted, 2026-10-24. Built in phase 2.**

## Decision

- All five AI providers (Claude, Gemini, OpenAI, custom servers, Ollama) are called with Lamha's one `HttpClient` (`Lamha.Core.Net.Http`) and `System.Text.Json` nodes, as Lamha 2 called them with `fetch`. No provider SDK.
- Claude uses the Messages API with `output_config.format` (a JSON Schema answer) and `output_config.effort`; Opus and Sonnet send the server-side fallback (`fallbacks: "default"`, beta `server-side-fallback-2026-07-01`). The models are Claude Opus 5.5 (the default), Sonnet 5.5 and Haiku 5.5 (the translator's default); a saved older model falls back to the default, as in Lamha 2.

## Why

- **NativeAOT.** Publishing the official Anthropic C# SDK (13.3.0) with NativeAOT gives trim and AOT warnings (IL2104, IL3053), and Lamha's rule is that a package publishes without warnings. `Microsoft.Data.Sqlite` was checked the same way and is clean.
- **One way to test.** Every provider, Google and Wikipedia go through the same fake network in the tests, with the same recorded answers, as Lamha 2's tests did.
- **Memory and size.** Five SDKs would load five HTTP and JSON stacks; Lamha needs one request shape per provider.

## Turned down

- The official SDKs: revisit if they become AOT-clean and Lamha needs something they do better (streaming events, tool use).
- OpenAI's Responses API: Chat Completions serves OpenAI and every compatible server with one code path (decision 0007).
