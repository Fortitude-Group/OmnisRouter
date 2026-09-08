# Phase 0 Research: Tray-managed local router proxy

All findings below are grounded in a code-level recon of the current OmnisRouter tray, server, key
store, client-rewrite CLI and release pipeline (Principle XI). File references are to the state of
`main` at the time of writing.

## Decision: supervise `omnisrouter.exe` as a hidden child process

- **Decision**: The tray launches the shipped server binary (`omnisrouter.exe`, bare invocation =
  serve) as a hidden `Process`, supervises it with a restart-with-backoff loop, and stops it on quit.
- **Rationale**: Reuses the entire router (routing model, ONNX embedder, upstream adapters, encrypted
  key store) with zero duplication, keeps the heavy ML runtime out of the WinForms process, and gives
  clean isolation and shutdown. It mirrors the existing collector supervision shape
  (`TrayContext.SuperviseAsync`), so the team pattern is already established.
- **Alternatives considered**: (a) Host Kestrel in-process inside the WinForms app by referencing
  `OmnisRouter.Api`, rejected, two entry points and the ONNX/EF stack loaded into the UI process for
  no user benefit, more coupling, harder to isolate a server crash. (b) Keep docker/CLI, rejected,
  that is exactly the friction this feature removes.

## Decision: loopback bind on a configurable port, default 127.0.0.1:8787

- **Decision**: Launch with `--urls http://127.0.0.1:<port>`; default port `8787`, user-configurable
  and validated; loopback only.
- **Rationale**: The server has no hardcoded port (no `UseUrls`/Kestrel section in code); it is set by
  `ASPNETCORE_URLS`/`--urls`. 8080 is avoided because a user may already run a docker router there.
  Loopback-only is a hard security requirement, the local proxy must never be reachable off-box.
- **Alternatives considered**: 8080 (clash risk), an ephemeral random port (harder to show the user
  and to write into client configs), binding all interfaces (rejected on security).

## Decision: a tray-generated router token, seeded via `Omnis__BootstrapToken`

- **Decision**: On first enable the tray generates a random router token, passes it as
  `Omnis__BootstrapToken` when it launches the server, stores it DPAPI-protected in `router.json`, and
  reuses it as the bearer for `/v1/keys` calls and as the auth token written into connected clients.
- **Rationale**: Management endpoints require `Authorization: Bearer <router-token>`; the bootstrap
  token seeds the first token when the token table is empty, solving the chicken-and-egg without a
  console step. One token is enough for a single-user local instance.
- **Alternatives considered**: Minting a separate token per client (unnecessary for a single-user
  loopback instance, more to manage); a fixed/known token (insecure).

## Decision: provider keys via the router's own management API, no second store

- **Decision**: The keys window calls `POST/GET/DELETE /v1/keys` on the loopback port. Keys live only
  in the router's AES-256-GCM store (`master.key` under `%APPDATA%\OmnisRouter\`). Providers offered:
  Anthropic, OpenAI, Gemini, OpenRouter.
- **Rationale**: The router already encrypts, persists and uses keys; duplicating that in the tray
  would fork behaviour and risk plaintext exposure (Principle I). `POST /v1/keys` takes
  `{provider, label, api_key}` and never echoes the key back, which matches the "never shown again"
  requirement for free.
- **Alternatives considered**: Writing the encrypted column directly from the tray (bypasses the
  router's cipher abstraction and versioning); a tray-side keyring (a second secret store to keep in
  sync).

## Decision: reimplement the client rewrite in C#, and add a real revert

- **Decision**: Port the `--write` semantics into a C# `OmnisRouter.ClientLink` library and add a
  revert the current CLI lacks. Targets: Claude Code (`~/.claude/settings.json` env merge), Codex
  (`~/.codex/config.toml` managed block), Cursor (show-only). Capture pre-connect state at connect
  time so revert restores it exactly.
- **Rationale**: The existing `--write` lives in the npm helper (Node, `installer/index.js`) and has
  no revert, only `.bak` files. Making a WinForms app shell out to Node would add a runtime
  dependency; the transforms are simple (JSON merge, a delimited TOML block) and belong in a tested
  C# unit. Revert is a genuine upgrade over the CLI.
- **Alternatives considered**: Shell out to `npx omnisrouter-cli --write` (adds a Node dependency, and
  still no revert); leave wiring manual (fails the "first-class" bar the owner set).

## Decision: report routed traffic to OmnisVigil and de-dupe connected clients

- **Decision**: When the proxy is on and a project key is configured, the router reports content-free
  receipts (with the savings delta) to OmnisVigil using the collector's project key. Any client
  connected to the proxy is excluded from the collector's scope; disconnecting returns it.
- **Rationale**: The router's receipts are the accurate, savings-bearing source for routed traffic;
  the transcript collector measures a different point and would double-count a routed client. The tray
  already tracks which clients it connected (`ConnectedClients`), so it is the natural exclusion set.
  Reuses the collector's project-key capture flow when none exists.
- **Alternatives considered**: Report with no dedupe (double counting, warned but not prevented, owner
  rejected); local-only, no OmnisVigil reporting (owner rejected, breaks the one-dashboard story).

## Decision: bundle the server binary + embedder in the per-user MSI

- **Decision**: The `windows` release job also publishes `OmnisRouter.Api` self-contained for
  `win-x64` and stages `omnisrouter.exe` plus its `config/`, `routing/` and `models/` (ONNX) sidecars
  into the tray publish folder before `wix build`. The MSI's existing `**` harvest packages them; no
  `.wxs` change.
- **Rationale**: "No docker, works offline" requires the server on disk at install. The MSI already
  globs the publish dir, so bundling is a staging step, not an installer rewrite. Per-user,
  no-elevation install is fine for a loopback server.
- **Alternatives considered**: Download the server on first enable (smaller installer, but needs a
  network fetch and a trust/verification story, and fails offline). Owner accepted the size cost of
  bundling.

## Resolved unknowns

- Server port configuration → `ASPNETCORE_URLS`/`--urls`, no appsettings key. Resolved.
- Server data/working dir → SQLite defaults to the process working directory; launch with an explicit
  working dir under `%APPDATA%\OmnisRouter\router\`. Resolved.
- Management auth → single bearer router-token scheme, bootstrap-seedable. Resolved.
- Client config targets and revert → documented per client in `contracts/client-link.md`. Resolved.
- Packaging path → extend the `windows` job; installer unchanged. Resolved.

No open NEEDS CLARIFICATION remain.
