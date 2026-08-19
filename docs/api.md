# OmnisRouter API

Base URL: your self-host address (default `http://localhost:8080`). All routes except `/health`,
`/readyz`, `/`, and `/ui` require `Authorization: Bearer <router-token>`. Errors are returned in the
**client's original wire format's** error shape (Anthropic / OpenAI / Gemini), so existing clients
handle them unchanged.

## Routed endpoints (base-URL-swap compatible)

| Client format | Endpoint | Streaming |
|---|---|---|
| OpenAI Chat Completions | `POST /v1/chat/completions` | `"stream": true` → OpenAI SSE chunks + `[DONE]` |
| Anthropic Messages | `POST /v1/messages` | `"stream": true` → Anthropic named-event SSE |
| Gemini generateContent | `POST /v1beta/models/{model}:generateContent` / `:streamGenerateContent` | the `:stream…` action streams |

The chosen model may live on a **different** provider than the request format. Capabilities that
can't cross faithfully (vision→non-vision, cache-pin→provider without it, thinking-signature→different
model, strict schema / parallel-same-tool→Gemini) are **refused with an explicit 4xx**, never silently
dropped. Remote image URLs are fetched + inlined when the target provider can't dereference them.

## Routing receipt headers (on every routed response)

| Header | Meaning |
|---|---|
| `X-Omnis-Model` | `provider/model_id` chosen |
| `X-Omnis-Confidence` | top-1 softmax confidence (0–1) |
| `X-Omnis-Cluster` | intent cluster id |
| `X-Omnis-Policy` | routing-model `policy_version` |
| `X-Omnis-Decision` | `Routed` \| `Escalated` |
| `X-Omnis-Reason` | `CheapestCapable` / `ConfidenceBelowFloor` / `SessionPinned` / … |
| `X-Omnis-Cost-Usd` · `X-Omnis-Cost-Delta-Vs-Big` | estimated cost · savings vs strongest candidate |
| `X-Omnis-Session-Pin` | `applied` \| `none` |
| `X-Omnis-Capability-Notice` | *(only on a non-fatal degradation, e.g. `remote_image_will_be_fetched`)* |

## Transparency & management

| Method + path | Purpose |
|---|---|
| `POST /v1/route` | Full routing decision (JSON per [routing-receipt.schema.json](../specs/001-omnisrouter/contracts/routing-receipt.schema.json)) — **no upstream call, no cost** |
| `GET /v1/analytics/routing-decisions` | NDJSON decision-log export; filters `from,to,cluster_id,decision,provider,limit,cursor`. Content-free: a non-reversible `request_hash`, never prompt/response text or keys |
| `GET /v1/models` | Advertised candidate model pool |
| `POST /v1/keys` | Add a BYOK provider key `{provider,label,api_key}` → `201 {id,provider,label,created_at}` (never echoes the key) |
| `GET /v1/keys` · `DELETE /v1/keys/{id}` | List (redacted) · delete |
| `GET /ui` | Self-host dashboard (spend, savings, recent decisions) |
| `GET /health` · `GET /readyz` | Liveness · readiness (DB reachable) |

## Auth & tenancy

- **Router tokens** authenticate clients; stored as SHA-256 hashes. Bootstrap the first token with the
  `Omnis:BootstrapToken` config value on startup (seeded only when the store has none).
- v1 is single-tenant (`default`); the schema and code carry `TenantId` for a future hosted layer.
