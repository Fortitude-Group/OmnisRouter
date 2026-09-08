# Contract: tray ↔ router management (consumed)

The tray consumes the router's existing loopback API and launch contract. Nothing here is new router
surface, it is documented so the tray's use of it is explicit and testable against a stub.

## Launch contract

The tray starts the server as a hidden child process:

- Executable: bundled `omnisrouter.exe` (bare invocation = serve).
- Arguments/env:
  - `--urls http://127.0.0.1:<port>` (loopback only).
  - Working directory `%APPDATA%\OmnisRouter\router\` (so `omnisrouter.db` lands there).
  - `Omnis__BootstrapToken=<router token>` (seeds the first management token when the token table is
    empty; harmless once seeded).
- The server runs its EF migration on startup, then serves.

## Readiness

- `GET /health` → 200 when the process is up.
- `GET /readyz` → 200 when the server is ready to serve (migrations applied). The tray polls this
  after launch, with a bounded timeout (default 30s), before reporting `Running` and enabling the keys
  and connect actions. These two paths plus `/`, `/ui` are the only unauthenticated routes.

## Provider keys (auth: `Authorization: Bearer <router token>`)

- `POST /v1/keys` body `{ "provider": "anthropic|openai|gemini|openrouter", "label": "...",
  "api_key": "..." }` → `201 { id, provider, label, created_at }`. The raw key is never echoed.
- `GET /v1/keys` → list of `{ id, provider, label, created_at, last_used_at }`, newest first, no key
  material.
- `DELETE /v1/keys/{id}` → `204`.

## Error handling the tray must implement

- Port already in use at launch → `Error` state, popup names the conflict, proxy stays off (FR-017,
  edge case).
- `/readyz` never passes within the timeout → `Error` state with the last failure surfaced.
- Non-2xx from `/v1/keys` → surface the message in the keys window, do not assume success.
- Child process exits unexpectedly → restart with backoff; repeated failure → `Error` (FR-006).
