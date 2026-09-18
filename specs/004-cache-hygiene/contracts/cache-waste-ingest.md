# Contract: `cache_waste` on the ingest record (the hand-off)

This is the content-free block OmnisRouter 004 adds to the receipts-up record and **freezes** for the
OmnisVigil dashboard spec to consume (FR-013). It extends `docs/omnisvigil-integration-contract.md` and
`docs/contracts/omnisvigil-ingest-record.schema.json`. Scalars and labels only — no bytes, diff, or keys.

## Shape

```json
"cache_waste": {
  "cause_class": "crlf_drift",
  "avoidable": true,
  "recomputed_tokens": 1234,
  "waste_gbp": 0.0123,
  "fix_applied": "line_ending",
  "saved_tokens": 1200,
  "saved_gbp": 0.0110,
  "pricing_version": "2026-08-15",
  "fx_date": "2026-08-15",
  "usd_gbp": 0.79,
  "shadow_price": false
}
```

## Fields

| Field | Type | Notes |
|---|---|---|
| `cause_class` | enum string | one of the `CauseClass` values (data-model.md) |
| `avoidable` | bool | false for `model_change` / `system_prompt_change` / `genuine_edit` |
| `recomputed_tokens` | int | tokens paid at write price on this miss (from real usage) |
| `waste_gbp` | number | avoidable-miss cost; **0 when unavoidable** (the reporting side relies on this) |
| `fix_applied` | enum string or null | `line_ending` / `trailing_whitespace` / `tool_ordering`, or null |
| `saved_tokens` | int | tokens the fix turned write → read (0 when no fix) |
| `saved_gbp` | number | value of `saved_tokens` at (write − read) |
| `pricing_version` | string | pricing snapshot date used |
| `fx_date` | string | date of the USD→GBP rate |
| `usd_gbp` | number | the rate applied |
| `shadow_price` | bool | true on a subscription: a shadow figure, never a bill |

## Rules

- **Optional block.** Absent when no analysis ran (measurement off, non-cacheable, first-in-lineage,
  or a dropped off-path analysis). A record without it is byte-identical to today's (FR-015, SC-007).
- **Content-free.** Every field is a scalar or a label. Enforced by a test over outbound records
  (FR-014, SC-002), the same posture as the existing allowlist test.
- **Two serialisers.** `IngestRecordMapper.ToRecord` (snake_case) and `AnalyticsDecisions.ToJson` must
  both emit it (the survey flagged them as divergent — keep in sync).
- **£ stamped.** Each £ carries `pricing_version` + `fx_date` + `usd_gbp` and the `shadow_price` marker
  (FR-016/FR-017).

## Compatibility (cross-repo)

Adding `cache_waste` as a new top-level member means an OmnisVigil that has not yet accepted it would
reject the whole record under its closed schema. Therefore: freeze this contract here, land its
acceptance on the Vigil side, and only then emit it into production (research D5). The router gates
emission until then.
