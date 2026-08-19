# `POST /v1/route` — Routing decision only

Returns the full routing decision for a request **without calling any upstream provider** and **without incurring cost** (FR-008). This is the transparency surface: parity with competitors' "which model would you pick" plus the richer receipt they don't publish.

## Request

- Auth: `Authorization: Bearer <router-token>`.
- Body: a request in **any** of the three supported wire formats (Anthropic / OpenAI / Gemini). The router normalizes and runs the routing pipeline (embed → nearest centroid → policy lookup → confidence gate → guardrails) but stops before dispatch.
- Optional query/header: `cost_tier` (`low|balanced|max`) to preview a different point on the cluster's ranked candidate list.

## Response `200`

Body conforms to [routing-receipt.schema.json](./routing-receipt.schema.json) — the same `ModelDecision` object surfaced (in compact header form) on real routed calls. Example:

```json
{
  "policy_version": "2026-08-15.3",
  "cluster_id": 41,
  "confidence": 0.58,
  "confidence_floor": 0.60,
  "top1_cosine_sim": 0.71,
  "top2_cosine_sim": 0.64,
  "margin": 0.07,
  "decision": "ESCALATED",
  "reason": "confidence_below_floor",
  "chosen": { "provider": "anthropic", "model_id": "claude-strong-default" },
  "alternatives": [
    { "provider": "openai", "model_id": "gpt-cheap", "predicted_quality": 0.82, "est_cost_usd": 0.0011, "est_cost_delta_usd": -0.0143 },
    { "provider": "anthropic", "model_id": "claude-mid", "predicted_quality": 0.90, "est_cost_usd": 0.0052, "est_cost_delta_usd": -0.0102 }
  ],
  "est_cost_usd": 0.0154,
  "est_cost_delta_vs_big_usd": 0.0,
  "session_pin_applied": false,
  "session_pin_reason": null,
  "pricing_snapshot_date": "2026-08-15"
}
```

## Guarantees

- **Zero upstream calls, zero token cost** — verifiable (no provider network egress during a `/v1/route` request).
- **Deterministic** for a fixed routing-model version + fixed input (same guarantee as the golden routing-model test, FR / Principle IV).
- Guardrail refusals (capability that can't cross) are reported here too: `reason: "capability_guardrail"` with the offending capability named, so a client can pre-check before sending real traffic.
