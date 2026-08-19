# Contracts: OmnisRouter v1

Public and internal contracts for feature `001-omnisrouter`. Public wire contracts are **stable, versioned** surfaces (Constitution II); the routing receipt in particular is treated as a public contract, not a debug artifact.

| File | Contract | Stability |
|---|---|---|
| [wire-formats.md](./wire-formats.md) | The three ingress/egress request formats + receipt response headers | Public — mirrors upstream provider wire formats; base-URL-swap compatible |
| [routing-receipt.schema.json](./routing-receipt.schema.json) | JSON Schema for a `ModelDecision` / receipt (shared by `/v1/route` body and decision-log rows) | Public, versioned by `policy_version` |
| [route-endpoint.md](./route-endpoint.md) | `POST /v1/route` — routing decision only, no upstream call | Public |
| [analytics-decisions.md](./analytics-decisions.md) | `GET /v1/analytics/routing-decisions` — NDJSON decision export | Public |
| [internal-interfaces.md](./internal-interfaces.md) | `IFormatAdapter`, `IRoutingPolicy`, `IEmbedder`, `IUpstreamClient`, `ISecretCipher`, `IMasterKeyProvider` | Internal — module boundaries (Constitution I) |

## Endpoint surface (FR references)

| Method + path | Purpose | FR |
|---|---|---|
| `POST /v1/messages` | Anthropic-format, routed | FR-001/002 |
| `POST /v1/chat/completions` | OpenAI-format, routed | FR-001/002 |
| `POST /v1beta/models/{model}:{action}` | Gemini-format, routed | FR-001/002 |
| `POST /v1/route` | Routing decision only (no upstream call) | FR-008 |
| `GET /v1/models` | Advertised candidate model pool | FR-016 |
| `GET /v1/analytics/routing-decisions` | NDJSON decision-log export | FR-009 |
| `GET /health`, `GET /readyz` | Liveness / readiness | FR-017 |
| `GET /ui` | Self-host dashboard (spend, savings, decisions) | FR-017 |

Every routed response (the first three rows) also carries the **receipt headers** defined in `wire-formats.md` (FR-007).
