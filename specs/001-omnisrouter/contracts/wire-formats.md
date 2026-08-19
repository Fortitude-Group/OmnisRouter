# Wire Formats & Receipt Headers

OmnisRouter is **base-URL-swap compatible** with three upstream wire formats. A client keeps its existing request/response shape; the router normalizes, routes, translates, and returns in the *same* format the client used (FR-001, FR-002). Adapters own all provider quirks; the neutral model in [../data-model.md](../data-model.md) is the pivot.

## Ingress formats (each routed)

| Client format | Endpoint | Response |
|---|---|---|
| Anthropic Messages | `POST /v1/messages` | Anthropic Messages response (streamed as Anthropic SSE if `stream:true`) |
| OpenAI Chat Completions | `POST /v1/chat/completions` | OpenAI chat completion (or `chat.completion.chunk` SSE + `[DONE]`) |
| Gemini generateContent | `POST /v1beta/models/{model}:generateContent` and `:streamGenerateContent` | Gemini `GenerateContentResponse` (or per-chunk SSE with `?alt=sse`) |

The chosen model may live on a **different** provider than the ingress format (cross-format routing). Streaming re-framing, tool-call id/schema differences, vision URL-vs-base64, caching, and thinking-signature handling follow the mapping and **guardrail rules** in [../research.md](../research.md) §R2. Where a requested capability cannot be faithfully preserved by the candidate provider, the router returns an explicit error (see below) rather than silently dropping it (FR-011).

## Authentication

- Client → router: `Authorization: Bearer <router-token>` (hashed at rest; see `RouterToken` in data-model).
- Router → upstream: operator's BYOK provider key, decrypted just-in-time (FR-013). Never logged or echoed (FR-014).

## Receipt headers (on every routed response — FR-007)

Compact receipt mirrored from the full schema ([routing-receipt.schema.json](./routing-receipt.schema.json)):

| Header | Meaning |
|---|---|
| `X-Omnis-Model` | `provider/model_id` chosen |
| `X-Omnis-Confidence` | top-1 softmax confidence (0–1) |
| `X-Omnis-Cluster` | intent cluster id |
| `X-Omnis-Policy` | `policy_version` of the routing model |
| `X-Omnis-Decision` | `ROUTED` \| `ESCALATED` |
| `X-Omnis-Reason` | decision reason enum |
| `X-Omnis-Cost-Usd` | estimated cost of the chosen model |
| `X-Omnis-Cost-Delta-Vs-Big` | estimated savings vs the strongest candidate |
| `X-Omnis-Session-Pin` | `applied` \| `none` |
| `X-Omnis-Capability-Notice` | *(present only when a non-fatal degradation was surfaced, e.g. `image_detail_dropped`, `reasoning_budget_approximated`)* |

The full decision (ranked alternatives + raw cosine sims + margin) is available via `POST /v1/route` and the decision-log export; headers carry the summary so no client parsing of the body is required.

## Error contract

Errors are returned **in the client's original format's error shape** (Anthropic `{"type":"error","error":{...}}`, OpenAI `{"error":{...}}`, Gemini `{"error":{...}}`) so existing clients handle them unchanged. Router-specific conditions map to stable codes:

| Condition | HTTP | Notes |
|---|---|---|
| Capability guardrail refusal (e.g. vision→non-vision, thinking-signature→different model) | 400 | error message names the dropped capability and the guardrail rule; **never** a silent downgrade |
| No valid BYOK key for the chosen model's provider | 400 | non-leaking message; no fallback to an unauthorized key |
| Upstream provider error/timeout | 502 / 504 | surfaced faithfully; decision-log `Outcome=upstream_error` |
| Client cancelled | (connection closed) | upstream call cancelled; `Outcome=cancelled`; no further billing |
