# Contract: the receipt cache block

The operator-facing surface: what the router found and fixed, per request. Two carriers, both fed from
`ModelDecision.Cache`, extending `docs/contracts/routing-receipt.schema.json`.

## `X-Omnis-Cache-*` response headers (`WriteReceiptHeaders`)

Emitted on a routed response when analysis ran:

| Header | Example | Meaning |
|---|---|---|
| `X-Omnis-Cache-Cause` | `crlf_drift` | the miss cause (omitted on a hit) |
| `X-Omnis-Cache-Avoidable` | `true` | whether it was avoidable |
| `X-Omnis-Cache-Recomputed-Tokens` | `1234` | tokens recomputed at write price |
| `X-Omnis-Cache-Waste-Gbp` | `0.0123` | avoidable-miss cost (0 when unavoidable) |
| `X-Omnis-Cache-Fix` | `line_ending` | the fix applied, if any |
| `X-Omnis-Cache-Saved-Tokens` | `1200` | tokens saved by the fix |
| `X-Omnis-Cache-Saved-Gbp` | `0.0110` | value saved |
| `X-Omnis-Cache-Pricing-Version` | `2026-08-15` | stamp |
| `X-Omnis-Cache-Fx-Date` | `2026-08-15` | stamp |
| `X-Omnis-Cache-Shadow` | `false` | shadow-price marker |

Headers carry no byte-level detail.

## `POST /v1/route` cache section (`ReceiptJson`)

The decide-only route response gains a `cache` object with the same figures **plus** the byte-level
before/after — because `/v1/route` is the synchronous, caller-owned receipt and the caller already owns
their content (FR-007):

```json
"cache": {
  "cause": "crlf_drift",
  "avoidable": true,
  "recomputed_tokens": 1234,
  "waste_gbp": 0.0123,
  "fix_applied": "line_ending",
  "saved_tokens": 1200,
  "saved_gbp": 0.0110,
  "pricing": { "pricing_version": "2026-08-15", "fx_date": "2026-08-15", "usd_gbp": 0.79, "shadow_price": false },
  "before_after": { "offset": 481, "before": "…\\r\\n", "after": "…\\n" }
}
```

## Rules

- The `before_after` detail appears **only** here (and in an operator-opt-in local log), never in the
  ingest record sent onward.
- The block is present only when analysis ran; a hit or an un-analysed request omits it.
- Every £ carries its stamp and shadow marker; on a subscription the figures are shadow estimates.
