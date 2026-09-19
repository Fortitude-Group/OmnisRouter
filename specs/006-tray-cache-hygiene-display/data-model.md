# Data Model: Cache hygiene in the tray

No persisted schema changes. These are in-memory and wire shapes only. All content-free.

## CacheHygieneTally (router, in-memory)

The running total the router accumulates as each `CacheHygieneResult` is produced. Thread-safe;
process-lifetime; resets on restart. Today counters reset at the local day boundary.

| Field | Type | Notes |
|---|---|---|
| SinceStart | period bucket | totals since the router process started |
| Today | period bucket | totals since local midnight; resets at the day boundary |
| MeasurementEnabled | bool | mirrors `CacheHygieneOptions.MeasurementEnabled` so the summary can say "off" |
| Pricing | pricing stamp | the latest `PricingStamp` seen (pricing version, FX date, USD/GBP, shadow flag) |

**Period bucket** (one per SinceStart / Today):

| Field | Type | Notes |
|---|---|---|
| AvoidableWasteGbp | decimal | sum of `CacheHygieneResult.WasteGbp` (already avoidable-only; unavoidable is 0) |
| RecoveredGbp | decimal | sum of `CacheHygieneResult.SavedGbp` |
| RecomputedTokens | long | sum of recomputed tokens (context for the £) |
| SavedTokens | long | sum of tokens a fix turned write into read |
| MissCount | int | number of avoidable misses counted |
| RecoveryCount | int | number of misses a fix recovered |

**Update rule**: on each non-null `CacheHygieneResult`, add to both buckets; when `WasteGbp > 0` count a
miss, when `SavedGbp > 0` count a recovery. Deterministic and additive (Principle IV).

## CacheHygieneSummary (wire: GET /v1/analytics/cache-hygiene/summary)

The content-free projection of the tally the tray reads. See
[contracts/cache-hygiene-summary.md](./contracts/cache-hygiene-summary.md).

| Field | Type | Notes |
|---|---|---|
| measurement_enabled | bool | when false, the tray shows "measurement off" |
| since_start | period object | avoidable_waste_gbp, recovered_gbp, recomputed_tokens, saved_tokens, miss_count, recovery_count |
| today | period object | same shape as since_start |
| pricing_version | string | date of the pricing snapshot behind the £ figures |
| fx_date | string (date) | date of the USD to GBP rate |
| usd_gbp | number | the rate applied |
| shadow_price | bool | true when the figures are subscription shadow prices, not a bill |
| source | enum `routed` \| `collect` | which workload the figures describe (routed traffic, or observed subscription) |

## FixState (wire: GET/PUT /v1/cache-hygiene/fixes)

The enabled byte-mutating fix classes, distinguishing local intent from the effective (policy-resolved)
state. See [contracts/cache-fixes-control.md](./contracts/cache-fixes-control.md).

| Field | Type | Notes |
|---|---|---|
| local | string[] | fix classes enabled in local config/runtime: `line_ending`, `trailing_whitespace`, `tool_ordering` |
| effective | string[] | what actually runs after any OmnisVigil policy override |
| policy_overrides | bool | true when a policy is present and authoritative over local |

**PUT body**: `{ "enabled": ["line_ending", ...] }` sets the local set. The response is the resulting
`FixState` (so the tray immediately sees whether the policy overrode it).

## Observed cache cost (collect mode, in-memory)

For US3, the collect engine accumulates observed cache-write tokens and their shadow cost. Content-free,
derived from the token counts `UsageEntry` already carries. Cause and avoidability are **not** modelled
here (collect mode has no prefix bytes to classify), so this bucket carries only:

| Field | Type | Notes |
|---|---|---|
| ObservedCacheWriteTokens | long | sum of `cache_creation_input_tokens` over the period |
| ShadowCostGbp | decimal | those tokens at the cache-write premium, shadow-priced |
| Period | since-start / today | mirrors the router tally's period split |

Surfaced through the summary with `source = collect`, `shadow_price = true`, and a label making clear it
is gross observed cache-write spend, not classified avoidable waste.
