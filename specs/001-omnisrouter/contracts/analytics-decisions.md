# `GET /v1/analytics/routing-decisions` — Decision-log export

Streams the persisted routing decision log as **NDJSON** (one JSON object per line) for the operator to audit, analyze, or feed the self-host dashboard (FR-009). Content-free by construction (FR-014): rows carry a non-reversible `request_hash`, never prompt or response text.

## Request

- Auth: `Authorization: Bearer <router-token>`.
- Query params:
  - `from`, `to` (ISO-8601 date/time; default: last 24h)
  - `limit` (default 10000), `cursor` (opaque, for pagination)
  - `cluster_id`, `decision` (`ROUTED|ESCALATED`), `provider` — optional filters
- `Accept: application/x-ndjson`.

## Response `200` (`application/x-ndjson`)

One `DecisionLogEntry` per line:

```
{"id":"...","timestamp":"2026-08-19T12:01:02Z","session_id":null,"request_hash":"b3f...","client_format":"openai","cluster_id":41,"chosen_provider":"openai","chosen_model_id":"gpt-cheap","confidence":0.83,"top1_sim":0.79,"top2_sim":0.55,"margin":0.24,"decision":"ROUTED","reason":"cheapest_capable","policy_version":"2026-08-15.3","est_cost_usd":0.0011,"est_cost_delta_vs_big_usd":-0.0143,"session_pin_applied":false,"outcome":"success","latency_ms":37}
{"id":"...","timestamp":"2026-08-19T12:01:05Z", ... ,"decision":"ESCALATED","reason":"confidence_below_floor", ... }
```

Field semantics match `DecisionLogEntry` in [../data-model.md](../data-model.md).

## Guarantees

- **Every routed request appears exactly once** (SC-004).
- **No prompt/response content, no keys** — only `request_hash` + decision metadata (FR-014, SC-007).
- Streamed (chunked NDJSON) so large exports don't buffer server-side; `cursor` continues an interrupted export.
- Figures are explainable: each row's cost uses the `policy_version`'s pinned pricing snapshot (Principle XII).
