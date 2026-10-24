# 0007. One OpenAI-compatible client for OpenAI and custom servers

**Accepted, 2026-10-24. Built in phase 2.**

## Decision

- Lamha 3's AI providers are Claude, Gemini, OpenAI, a custom server, and Ollama, behind one `IAiProvider` interface.
- OpenAI and custom servers (LM Studio, OpenRouter, Groq, Azure OpenAI…) share one client speaking OpenAI's **Chat Completions** API. The model list comes from `/v1/models`.
- A custom server may use plain `http://` only on `localhost` or the local network; anything else must be `https://`, so a key is never sent in the clear.

## Why

One code path serves every compatible server, and Chat Completions is what they all speak. OpenAI itself can move to its Responses API later if it offers something Lamha needs.

## Consequences

- Recorded answers from LM Studio, OpenRouter and Groq in the tests, because "compatible" servers differ in small ways (streaming, model lists, errors).
- In 3.0, fallback stays between Google and the AI, as in 2.x; fallback between AI providers comes later.
