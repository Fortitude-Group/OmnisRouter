# Quickstart: Tray-managed local router proxy

How to validate the feature end to end. Automated coverage runs in CI; the full path with a real
router, a real provider key and a real client request is verified by hand on Windows, consistent with
how the tray and live sign-in are validated today.

## Prerequisites

- A Windows 10/11 x64 account.
- The built MSI for this feature (bundles the tray and the `omnisrouter.exe` server + embedder).
- One real provider API key (Anthropic, OpenAI, Gemini or OpenRouter) for the manual end-to-end.
- Optional: an OmnisVigil project key, to validate dashboard reporting (US4).

## Automated checks (CI)

- `dotnet test` covering:
  - `OmnisRouter.ClientLink.Tests`: golden connect + revert for Claude Code and Codex, the Cursor
    show-only output, idempotent re-connect, and the malformed-config refusal (per
    `contracts/client-link.md`).
  - Supervision and keys against a stub loopback server: readiness gating, crash-restart with backoff,
    port-in-use → error, and the `/v1/keys` request shapes (per `contracts/router-management.md`).
  - Collector dedupe: a connected client is excluded from collector scope and returns on disconnect.

## Manual end-to-end (Windows)

1. **Install**: run the MSI on a clean account. Confirm the tray icon appears and no console window
   is shown. (US1)
2. **Enable routing**: open the tray menu, turn on "Local router proxy". Accept the first-time
   confirmation that routed traffic is BYOK per-token billing. Confirm the tray moves through
   starting to running within a few seconds, and the icon shows the distinct routing-live treatment.
   (US1, FR-007)
3. **Add a key**: the keys window opens because no key is set. Paste a real provider key and save.
   Confirm the provider shows "key set" and the value is not shown back. (US2)
4. **Connect Claude Code**: open "Connect an app", click Connect for Claude Code. Confirm
   `~/.claude/settings.json` now has the router base URL and token under `env`, and a `.bak` was
   written. (US3)
5. **Route a request**: make a request from Claude Code and confirm it is served by the local router
   (a routing decision is recorded; the response returns normally).
6. **Check the dashboard** (if a project key is set): confirm the routed traffic and its savings
   appear once on the OmnisVigil dashboard, attributed to the router, and that Claude Code is not also
   double-counted by the collector. (US4)
7. **Revert**: click Revert for Claude Code. Confirm `settings.json` is byte-for-byte its pre-connect
   state. (US3, SC-004)
8. **Change the port**: set a different valid port in settings. Confirm the router restarts on it and
   any still-connected client is re-pointed. (US5)
9. **Relogin**: log out and back in. Confirm the proxy returns to the state it was in. (US1, SC-006)
10. **Disable with a client connected**: turn the proxy off while a client is connected. Confirm the
    tray warns and offers to revert it. (FR-018)

## Expected outcomes

- No console window at any step (SC-002).
- The active mode is always identifiable from the tray (SC-003).
- Revert restores exact prior config (SC-004).
- No client is counted twice on the dashboard (SC-005).
- A saved key is never shown back (SC-007).
