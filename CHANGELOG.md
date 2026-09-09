# Changelog

All notable changes to OmnisRouter are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - unreleased

### Added

- Local router proxy in the Windows tray. A second toggle, independent of the usage collector, runs
  OmnisRouter itself on `127.0.0.1` and supervises it as a hidden background process, so you route
  through your own provider keys with no Docker and no console. The icon shows when routing is live,
  and the first enable confirms what per-token BYOK billing means.
- Provider keys window to add Anthropic, OpenAI, Gemini or OpenRouter keys straight into the router's
  encrypted store. A stored key is never shown back.
- "Connect an app" to wire Claude Code, Codex or Cursor to the local router in one click, with an
  exact revert that restores each tool's previous settings from state captured at connect time.
  Claude Code and Codex are written to disk with a backup first; Cursor shows the values to paste.
- Routed spend reported to OmnisVigil when a project key is set, carrying the saving against the
  frontier model. A tool connected to the proxy is dropped from the collector at the same time, so
  the same spend is never counted twice.
- A configurable local port (default `8787`). Changing it restarts the router and re-points every
  connected app to the new address.
