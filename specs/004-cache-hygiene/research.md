# Phase 0 Research: Cache hygiene

Grounded in a fresh survey of the request path, receipt, ingest mapper, adapters, pricing, and the
sibling CacheScope tool. Every decision below is checked against the actual code (file anchors given),
per constitution XI. The brainstorm already locked the five product decisions; this resolves the
technical unknowns.

## D1 — Analyse the egress wire bytes, not the client body

**Decision**: Compute the cache prefix and apply any normalisation on the **egress wire
serialisation** of the neutral request, not on the incoming client body.

**Rationale**: The raw client `JsonElement body` is held for the whole call
(`RoutedRequestHandler.cs:46`), but the bytes actually sent upstream are a fresh per-provider
re-serialisation of the neutral `ChatRequest` via `AnthropicRequestMapper.ToWireRequest`
(`OmnisRouter.Upstream/Providers/AnthropicRequestMapper.cs`). What the provider caches is the wire
bytes, so both the measured prefix and any fix must target that output. For Anthropic the prefix is
the wire bytes up to and including the block carrying `cache_control`
(`AnthropicRequestMapper.cs:53-66`, `ToWireCacheControl`), which is an explicit marker — the prefix is
exact.

**Alternatives rejected**: Analysing the client `body` — it is not what gets cached, and OpenAI/Gemini
would diverge further; would produce a prefix that does not match the provider's cache key.

## D2 — Real usage carries the cache breakdown; analyse after it returns

**Decision**: Run the analysis off the hot path, after the response, where the real `Usage` is known.

**Rationale**: `Usage` already carries `CacheCreationTokens`/`CacheReadTokens` end to end
(`OmnisRouter.Core/Model/ChatResponse.cs:4-10`), mapped from Anthropic
(`AnthropicResponseMapper.cs:54-61`; streaming `AnthropicStreamState.cs`). Non-stream usage reaches
`BuildLogEntry` (`RoutedRequestHandler.cs:112`); streaming usage arrives on `StreamMessageStop` and is
logged in the iterator `finally` (`:157-175`). `CaptureAndLog`/`BuildLogEntry` (`:140-236`) is the one
place with both the request and the real usage — the correct analysis hook. This makes the figure
measured, not predicted (FR-003).

**Alternatives rejected**: Predicting from the static request before dispatch — the design keeps the
static prediction only for the offline no-traffic case, clearly labelled.

## D3 — Off-path mechanism: bounded, drop-on-full, after the response

**Decision**: The analysis runs after the response is delivered, on a bounded background path (a
capacity-bounded queue that drops rather than blocks when full). Normalisation, which must happen
in-path before dispatch, is cheap and runs within a small time budget; if it exceeds the budget or
throws it is skipped.

**Rationale**: Guarantees fail-open (FR-019/FR-020, SC-003): the request is already served before the
analysis runs, and a full queue drops the analysis rather than applying backpressure. A dropped
analysis simply yields no cache fields for that request — acceptable, since measurement is best-effort.

**Alternatives rejected**: Synchronous in-path analysis — risks latency and a failure path on the hot
path. An unbounded queue — could grow without bound under load, a memory risk.

## D4 — Lineage key: the session-pin identity, with a hash fallback

**Decision**: Group consecutive requests into a cache lineage by the **session identity already used
for session pinning**. When absent, fall back to a stable hash of the request's stable structure
(system + tools) so that a repeated shape still diffs. The lineage cache is an in-memory dictionary
keyed by this, bounded by entry count and age, LRU-evicted.

**Rationale**: Session pinning already exists (the router pins a session to a model, and the receipt
carries a session-pin signal), and a coding session is exactly the run of requests that share a cached
prefix — the natural lineage. It requires no new client contract. The hash fallback covers callers
that do not send a session identity.

**Alternatives rejected**: A new client-supplied lineage header — adds a contract and burden on the
caller. `request_hash` ancestry alone — the hash changes on every request by design, so it cannot key
a lineage; only its *stable-structure* subset can.

## D5 — Ingest contract compatibility and cross-repo deploy ordering

**Decision**: The router emits the `cache_waste` block **only when cache analysis produced a result
and emission is enabled**, and the block is added to the OmnisVigil ingest schema as an accepted member
**before** the router emits it into production. The block itself is content-free scalars and labels;
the OmnisVigil side has independently chosen to ignore *unknown members inside* `cache_waste`
(forward-compatible) while keeping the record boundary strict.

**Rationale**: The ingest path enforces content-free by a closed schema — Vigil rejects a record with
an unknown member and flags it (`docs/omnisvigil-integration-contract.md:49-55`;
`IngestRecordMapper.cs`). A new top-level `cache_waste` member sent to an *old* Vigil would be rejected,
dropping the whole receipt (spend included). So the contract must be frozen here and accepted on the
Vigil side before the router emits it in prod. This satisfies constitution X (do not break the
production ingest) without a prod apply in this repo.

**Alternatives rejected**: Emitting unconditionally and relying on Vigil tolerating it — would drop
real receipts against any Vigil that has not yet deployed the schema change.

## D6 — Pricing gains FX; the pound is stamped

**Decision**: Add `usd_gbp` + `fx_date` to the dated pricing snapshot yaml and a `PricingStamp`
(`pricing_version` = snapshot date, `fx_date`, `usd_gbp`, measured/shadow). `PricingBook` gains a GBP
path: £ = USD × `usd_gbp`, every figure carrying the stamp.

**Rationale**: `PricingBook` prices input/output/cache-read/cache-write separately
(`PricingBook.cs:80-100`) and is dated/immutable (`SnapshotDate`, `:64,102-128`), but is **USD-only** —
no FX anywhere. The sibling tool's `PricingStamp` (`ProseWeightVisualizer/.../contracts.py:268-292`:
`pricing_version` + `effective_date` + `fx_date` + `usd_gbp`) is the ready-made shape. Dated FX keeps
it deterministic and reproducible (FR-018), no network dependency.

**Alternatives rejected**: A live FX fetch — a network dependency, a failure mode, nondeterminism,
against fail-open. USD-only with Vigil converting — the design wants the £ stamped with its `fx_date`
at source.

## D7 — Cause classes: port the sibling enum

**Decision**: Reuse the sibling tool's `CauseClass` set as a C# enum: `crlf_drift`,
`trailing_whitespace`, `volatile_header`, `concat_order_change`, `timestamp_injection`,
`tool_definition_churn`, `model_change`, `system_prompt_change`, `genuine_edit`. `model_change`,
`system_prompt_change`, and `genuine_edit` are unavoidable.

**Rationale**: Matches the spec's cause set and keeps the router and the sibling analysis in agreement
(`ProseWeightVisualizer/.../contracts.py:62-72`). The result/capture contract versions
(`RESULT_VERSION`/`CAPTURE_CONTRACT_VERSION` = 1.0.0) are pinned; the C# port carries a conformance test
against the sibling's vectors so the two never drift.

## D8 — Normaliser safety proofs (response-transparency)

**Decision**: Each fix is applied only where meaning-preservation is demonstrable, proven by a
response-transparency test (SC-004):

- **Line-ending** — normalise CRLF/CR to LF in text content. Safe: LF vs CRLF is not semantically
  meaningful to the model; the test sends the same request with mixed and normalised endings and
  asserts equivalent responses.
- **Trailing whitespace** — strip trailing spaces/tabs per line. Safe by the same argument.
- **Tool-definition ordering** — sort tool definitions and canonicalise their JSON key order, applied
  **only** where the wire format treats tools as an unordered set. The model is given the same set of
  tools; the test asserts equivalent tool-selection behaviour.

**Rationale**: These are the three classes the design names as provably safe; prose, message content,
and anything meaning-bearing are warn-only and never mutated (FR-009).

## D9 — DecisionLogEntry migration

**Decision**: Add the cache-waste columns to `DecisionLogEntry`
(`OmnisRouter.Core/Routing/DecisionLog.cs:18-71`, which already persists
`ActualCacheCreationTokens`/`ActualCacheReadTokens`) and generate a SQLite + Npgsql migration.

**Rationale**: The decision log is EF-persisted; new columns need migrations in both providers. The
`AttributionTags` migration (`Store.Migrations.*/…AttributionTags.cs`) is the precedent to mirror.
Two serialisers must both learn the fields: `IngestRecordMapper.ToRecord` and `AnalyticsDecisions.ToJson`
(the survey flagged them as divergent).

## Resolved unknowns

The spec carried no `NEEDS CLARIFICATION` markers. The plan's remaining technical unknowns — the
lineage key (D4), the off-path mechanism (D3), and the cross-repo ordering (D5) — are resolved above.
