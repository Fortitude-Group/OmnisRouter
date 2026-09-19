# Contract: cache-hygiene summary endpoint

`GET /v1/analytics/cache-hygiene/summary` — the content-free headline figures the tray shows. Read from
the router's in-memory tally, O(1), no query. Loopback management surface, alongside `/v1/keys` and
`/v1/analytics/routing-decisions`.

## Response 200 (application/json)

```json
{
  "measurement_enabled": true,
  "source": "routed",
  "since_start": {
    "avoidable_waste_gbp": 0.0412,
    "recovered_gbp": 0.0110,
    "recomputed_tokens": 4123,
    "saved_tokens": 1200,
    "miss_count": 7,
    "recovery_count": 2
  },
  "today": {
    "avoidable_waste_gbp": 0.0180,
    "recovered_gbp": 0.0110,
    "recomputed_tokens": 1800,
    "saved_tokens": 1200,
    "miss_count": 3,
    "recovery_count": 2
  },
  "pricing_version": "2026-08-15",
  "fx_date": "2026-08-15",
  "usd_gbp": 0.79,
  "shadow_price": false
}
```

## Rules

- **Content-free.** Scalars and labels only. No prompt text, diff, or key. Enforced by a test over the
  response (mirrors 004's outbound-record posture).
- **Every number explained.** `avoidable_waste_gbp` and `recovered_gbp` are GBP; each period object states
  the period it covers by its key (`since_start` / `today`); the £ figures are backed by
  `pricing_version` + `fx_date` + `usd_gbp` and marked `shadow_price` true/false (Principle XII).
- **Avoidable-only waste.** `avoidable_waste_gbp` sums `CacheHygieneResult.WasteGbp`, which 004 already
  zeroes for unavoidable causes, so the figure is safe to read as avoidable spend.
- **Measurement off.** When `measurement_enabled` is false the period figures are zero and the tray shows
  "measurement off" rather than implying it measured and found nothing.
- **Source.** `routed` for the tray-hosted router's routed traffic; `collect` for observed subscription
  usage (gross cache-write shadow cost, no cause classification, `shadow_price` true).
- **Fresh start.** Before any analysis has run, all counters are zero and `miss_count` is 0; the tray
  reads this as "nothing measured yet".
