# Phase 1 Data Model: Tray-managed local router proxy

State the tray owns. The router's own entities (provider keys, router tokens, routing decisions) are
unchanged and are referenced, not redefined, here.

## RouterProcessState (in-memory)

The tray's view of the supervised server. Not persisted.

| Value | Meaning |
|---|---|
| `Off` | Proxy toggle is off, no server process running. |
| `Starting` | Server launched, readiness check not yet passed. |
| `Running` | Server up and `/readyz` passed. |
| `Error` | Repeated start/restart failure, or port conflict. |

**Transitions**: Off → Starting (user enables) → Running (readiness passes) or Error (timeout /
port in use). Running → Starting (crash, auto-restart) → Running/Error. Running/Error/Starting → Off
(user disables or app quits).

## RouterSettings — `%APPDATA%\OmnisRouter\router.json`

Tray-owned, kept separate from the collector's `collect.json` (FR-019). JSON, camelCase, indented.

| Field | Type | Rules |
|---|---|---|
| `schemaVersion` | int | Current schema version, for forward migration. |
| `enabled` | bool | Whether the proxy should run (restored at login). |
| `port` | int | Loopback port. Valid range 1024–65535. Default `8787`. |
| `protectedToken` | string | DPAPI-protected (CurrentUser) base64 of the router token. Never plaintext. Generated once on first enable. |
| `connectedClients` | ConnectedClient[] | The set the tray has wired (for revert and collector dedupe). |

Validation: `port` must be in range and not otherwise reserved; an invalid value is rejected at save
(FR-017). `protectedToken` decrypts only for the current Windows user (DPAPI), matching the
collector's key handling.

## ConnectedClient (element of `connectedClients`)

One coding tool the tray has wired to the local router.

| Field | Type | Rules |
|---|---|---|
| `client` | enum | `ClaudeCode` \| `Codex` \| `Cursor`. |
| `connectedAt` | timestamp | When it was connected. |
| `priorState` | object | Captured pre-connect state needed for exact revert (see `contracts/client-link.md`): for Claude Code, the prior presence/value of the two env keys; for Codex, whether a managed block already existed; for Cursor, none (show-only, tracked as user-confirmed). |
| `backupPath` | string? | Path to the timestamped backup written before editing (Claude Code, Codex). |

Membership in this list is the collector's exclusion set (FR-015): a client here is not tailed by the
collector; removing it returns the client to collector scope.

## RouterManagementToken (secret, held as `protectedToken`)

The single bearer credential for the local router. Generated once by the tray (high-entropy random),
seeded into the server via `Omnis__BootstrapToken` on first launch, and reused as: the bearer for
`/v1/keys` calls, and the auth token written into connected clients. Rotating it means reseeding the
server and re-pointing connected clients.

## Referenced (router-owned, unchanged)

- **ProviderKey**: `{ id, provider, label, createdAt, lastUsedAt }`, key material encrypted
  (AES-256-GCM), never returned. Providers: `anthropic | openai | gemini | openrouter`. Created via
  `POST /v1/keys`, listed via `GET /v1/keys`, removed via `DELETE /v1/keys/{id}`.
- **Routing receipt**: content-free per-request record the router already emits; when OmnisVigil
  reporting is on it is posted to the dashboard with the project key. Carries the savings delta.
