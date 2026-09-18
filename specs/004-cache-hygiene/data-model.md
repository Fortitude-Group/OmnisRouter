# Phase 1 Data Model: Cache hygiene

The in-process and on-the-wire entities the feature introduces. Types live in `OmnisRouter.CacheHygiene`
unless noted. No new persistent store except columns added to the existing `DecisionLogEntry`.

## CauseClass (enum)

The reason a cached prefix missed. Ported from the sibling tool (research D7).

| Value | Avoidable | Meaning |
|---|---|---|
| `crlf_drift` | yes | line endings changed (CRLF/CR vs LF) |
| `trailing_whitespace` | yes | trailing spaces/tabs changed |
| `volatile_header` | yes | a volatile header before the breakpoint moved |
| `timestamp_injection` | yes | a timestamp before the breakpoint moved |
| `concat_order_change` | yes | machine-generated structure serialised in a different order |
| `tool_definition_churn` | yes | tool set/JSON reordered though treated as a set |
| `model_change` | no | a deliberate model change |
| `system_prompt_change` | no | the system prompt genuinely changed |
| `genuine_edit` | no | the content genuinely changed |

## CacheHygieneResult (in-memory)

The analyzer's output for one request. Immutable record; a C# port of the sibling RESULT contract so the
two agree (conformance-tested).

| Field | Type | Meaning |
|---|---|---|
| `Missed` | bool | whether the cached prefix missed at all |
| `Cause` | `CauseClass?` | the cause, when missed |
| `Avoidable` | bool | derived from `Cause` |
| `DivergenceOffset` | long? | first divergent byte between this prefix and the previous one |
| `RecomputedTokens` | int | tokens paid at write price on this miss (from real usage) |
| `FixApplied` | `FixClass?` | the fix that ran, if any |
| `SavedTokens` | int | tokens the fix turned write → read (0 if no fix) |
| `Pricing` | `PricingStamp` | the stamp for every £ below |
| `WasteGbp` | decimal | `RecomputedTokens` priced at the write premium **when avoidable**, else 0 |
| `SavedGbp` | decimal | `SavedTokens` priced at (write − read), when a fix ran |
| `ResultVersion` | string | the sibling RESULT contract version this conforms to |

**Rules**: `WasteGbp` is 0 on an unavoidable miss by construction (the reporting side depends on this).
The saving is grounded in real usage: a fix's `SavedTokens` are confirmed by the provider returning a
read on the normalised prefix.

## PricingStamp

Attached to every £ figure (research D6).

| Field | Type | Meaning |
|---|---|---|
| `PricingVersion` | string | the pricing snapshot date used |
| `FxDate` | string | the date of the USD→GBP rate |
| `UsdGbp` | decimal | the rate applied |
| `ShadowPrice` | bool | true on a flat-rate subscription: a shadow estimate, never a bill |

## FixClass + CacheHygieneOptions

| `FixClass` | Transform | Default |
|---|---|---|
| `LineEnding` | normalise CRLF/CR → LF in text content | **off** |
| `TrailingWhitespace` | strip trailing spaces/tabs per line | **off** |
| `ToolOrdering` | sort tools + canonicalise tool-JSON key order (set-safe only) | **off** |

`CacheHygieneOptions`: `MeasurementEnabled` (default **true**), a per-`FixClass` enabled set (default
empty), the in-path normalisation time budget, and the lineage-cache size/age bounds. Fix enablement is
also settable by an OmnisVigil policy, the way caps/kill-state already are (FR-012).

## LineageEntry (in-memory, `LineageCache`)

| Field | Type | Meaning |
|---|---|---|
| `LineageKey` | string | session-pin identity, else a stable-structure hash (research D4) |
| `PrefixHashChain` / `PrefixBytes` | — | the previous prefix representation, enough to find the first divergent byte |
| `LastSeenUtc` | DateTimeOffset | for age eviction |

Bounded by entry count and age, LRU-evicted. **Never persisted** (the retention decision). Holds only the
previous prefix long enough to diff the next request.

## CacheWaste (the content-free ingest block) — the frozen contract

Added to `DecisionLogEntry` (new columns, migrated) and emitted by `IngestRecordMapper.ToRecord` and
mirrored in `AnalyticsDecisions.ToJson`. See `contracts/cache-waste-ingest.md` for the exact wire shape.
Scalars and labels only:

`cause_class`, `avoidable`, `recomputed_tokens`, `waste_gbp`, `fix_applied` (or null), `saved_tokens`,
`saved_gbp`, `pricing_version`, `fx_date`, `usd_gbp`, `shadow_price`. Optional block: absent when no
analysis ran.

## Receipt cache block

`ModelDecision` gains a `Cache` property (see `contracts/receipt-cache-block.md`): the same figures as
the ingest block **plus** the byte-level before/after, which appears only in the synchronous
caller-owned receipt, never onward (FR-007).
