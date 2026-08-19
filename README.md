# OmnisRouter

**Open-core, drop-in LLM routing proxy** — routes each request to the cheapest *capable* model with
**fully open, reproducible routing** and a **per-request routing receipt** that shows exactly which
model was picked, how confident the router was, what alternatives it considered, and what you saved.

Point an existing client (Claude Code, Codex, Cursor, or any app) at OmnisRouter with a base-URL
change; it accepts **Anthropic Messages**, **OpenAI Chat Completions**, and **Gemini** formats,
routes to the cheapest capable model in your pool — possibly on a *different* provider — translates
faithfully (streaming, tools, vision, prompt caching, extended thinking), and returns the response in
your client's original format.

The transparency wedge: the routing model (in-process embedding → intent cluster → published policy
table) is **versioned and reproducible from public data**, and its `policy_version` is stamped into
every decision. No black-box centroids, no unpublished savings claims.

## Why

- **Cheapest capable, not cheapest.** A confidence floor escalates hard prompts to a strong model —
  and the escalation is visible in the receipt.
- **Receipts on every request** (`X-Omnis-*` headers + `POST /v1/route` + an NDJSON decision log) —
  see [contracts](./specs/001-omnisrouter/contracts/).
- **Never picks a model it can't call** — routes only to providers you've given a BYOK key.
- **Refuse, don't silently drop** — if a candidate can't honor a capability (vision, cache pin,
  thinking-signature continuity, strict schema), the router returns an explicit error.
- **BYOK, encrypted at rest** (AES-256-GCM); prompts leave your infra only to the chosen upstream.
- **Single process + SQLite** self-host (Postgres optional); one binary or `docker compose`.

## Endpoints

| Route | Purpose |
|---|---|
| `POST /v1/chat/completions` · `POST /v1/messages` · `POST /v1beta/models/{model}:{action}` | Routed, in each wire format |
| `POST /v1/route` | Full routing decision, **no upstream call, no cost** |
| `GET /v1/analytics/routing-decisions` | NDJSON decision-log export (content-free) |
| `GET /v1/models` · `POST/GET/DELETE /v1/keys` | Candidate pool · BYOK key management |
| `GET /ui` · `GET /health` · `GET /readyz` | Self-host dashboard · probes |

See [`docs/api.md`](./docs/api.md) for the full surface + receipt headers.

## Getting started

```bash
# Docker
docker compose -f deploy/docker-compose.yml up -d --build

# or bare metal (.NET 10)
dotnet run --project src/OmnisRouter.Api      # http://localhost:8080
```

Then set a bootstrap token (`Omnis:BootstrapToken`), add a BYOK key (`POST /v1/keys`), and point your
client's base URL at the router (`npx omnisrouter-cli --url ... --token ...`). Full operator guide:
[`docs/self-host.md`](./docs/self-host.md).

## Reproducible routing model

The routing model is built by a documented, deterministic offline job — see
[`routing/BUILD.md`](./routing/BUILD.md). Same inputs → byte-identical model. Each tagged release must
pass the gate in [`scripts/release-gate.ps1`](./scripts/release-gate.ps1) (clean build + green tests +
a passing OmnisBench run) and publishes its benchmark frontier.

> **v1 note:** the shipped model uses a deterministic hashing embedder and a small sample dataset so
> the *pipeline* is fully reproducible today; pinning the ONNX `bge-small-en-v1.5` embedder and
> feeding real [OmnisBench](https://github.com/Fortitude-Group/OmnisBench) results makes routing
> semantically strong (tracked follow-ups). See [`docs/calibration.md`](./docs/calibration.md).

## Build & test

```bash
dotnet build OmnisRouter.slnx -c Release   # 0 warnings / 0 errors
dotnet test  OmnisRouter.slnx              # all green
```

## License

Apache-2.0 — see [LICENSE](./LICENSE). Copyright 2026 Fortitude Omnis Group.
