# Research: Cache hygiene in the tray

Phase 0 decisions. Each is grounded in the current code, not assumed.

## D1: How the tray gets the numbers — summary endpoint, not the NDJSON stream

**Decision**: Add a small content-free summary endpoint on the router,
`GET /v1/analytics/cache-hygiene/summary`, returning aggregate figures. The tray polls it over the
loopback `RouterManagementClient` it already uses for `/v1/keys`.

**Rationale**: The only cache-aware surface today is `GET /v1/analytics/routing-decisions`, which streams
one NDJSON row per decision (with the `cache_waste` block added in 004). Streaming and summing thousands
of rows on every popup open is wasteful and slow. Aggregation belongs on the router where the data lives,
and a popup wants two headline numbers, not a row set.

**Alternatives considered**: Client-side aggregation over the NDJSON stream (rejected: heavy, grows with
history, duplicates arithmetic the router can do once). A new persisted summary table (rejected:
unnecessary storage for a per-process running total).

## D2: Where the summary comes from — an in-memory running tally

**Decision**: The router keeps a thread-safe in-memory tally (`CacheHygieneTally`) updated each time
`CacheHygieneService` produces a `CacheHygieneResult`. It accumulates avoidable waste (£), recovered
saving (£), recomputed/saved tokens, and miss/recovery counts, split into a since-start total and a
today total that resets at the local day boundary (mirroring `CollectionStatus.TodayReceipts`).

**Rationale**: The routed path already computes the result; folding it into a counter is O(1) and adds no
hot-path cost or query. A process-lifetime tally matches the popup's "since start / today" framing and
needs no storage. Restart resets it, which is honest and expected for a live tray.

**Alternatives considered**: Aggregating the decision-log table on demand (rejected: a DB scan per popup;
the log is the durable record, the tally is the cheap live view). Persisting the tally (rejected:
over-engineering for a desktop live view; the durable history is already the decision log and OmnisVigil).

## D3: Toggling fixes from the tray — a runtime management endpoint, policy still wins

**Decision**: Add `GET/PUT /v1/cache-hygiene/fixes`. `PUT` updates the router's in-memory enabled-fix set
at runtime; `GET` returns both the local set and the effective set (after any OmnisVigil policy override).
The tray shows the effective state and, when a policy overrides local config, marks the local toggle as
not authoritative (FR-010).

**Rationale**: FR-009 requires the toggle to take effect without a config edit or restart. The precedence
already exists in code: `CacheHygieneService.IsFixEnabled` consults `IFixPolicy` (the Vigil-backed
override) before the local options. The endpoint sets the local layer; the policy layer keeps winning
where present, so the tray must display the resolved truth, not just the local intent.

**Alternatives considered**: Tray writes the config file and restarts the router (rejected: slow, drops
in-flight requests, and fights the policy layer). A tray-only setting the router never sees (rejected:
would not change behaviour).

## D4: Collect-mode cache hygiene (US3) — a coarse observed figure only

**Decision**: In collect mode the tray shows the shadow cost of observed cache re-writes:
`cache_creation_tokens x cache-write premium`, shadow-priced, labelled as gross observed cache-write
spend, not avoidable-only waste. It does **not** classify cause (CRLF drift, genuine edit, etc.) or split
avoidable from unavoidable.

**Rationale**: Verified in the code: `CollectEngine.ProcessAsync` already reads `CacheReadTokens` and
`CacheCreationTokens` per entry, so the token signal is present. But `UsageEntry` (and the
`TranscriptReader` that builds it) is content-free by design ("usage fields are read, never the message
text"), so collect mode never sees the system/tools prefix bytes that `CacheHygieneAnalyzer.Classify`
needs to diff prefixes and decide the cause. Without the prefix it cannot tell a legitimate first-write
from an avoidable re-write. The honest figure is therefore the gross cache-write shadow cost, clearly
labelled, never dressed up as classified avoidable waste. This keeps content-free intact (Principle IV)
and every number honest (Principle XII).

**Alternatives considered**: Reading prefix bytes from transcripts to classify (rejected: breaks the
content-free guarantee that is the whole point of collect mode). Hiding cache hygiene entirely in collect
mode (rejected: the watcher user is the tray's original audience and the gross shadow cost is still
useful and honest).

## D5: What each number must say (Principle XII)

**Decision**: Every figure the tray shows carries its unit, the period it covers, and for money the
pricing snapshot date, the FX date, the USD/GBP rate basis, and whether it is a real bill (pay-as-you-go)
or a subscription shadow figure. This is consistent with 004's receipt and the OmnisVigil `cache_waste`
block, so the tray never invents a number the router cannot back.

**Rationale**: Constitution Principle XII, and the spec's FR-002/FR-003 and SC-002. The pricing/FX/shadow
fields already exist on `CacheHygieneResult.Pricing` (`PricingStamp`), so the summary just carries them.

## D6: Fail-open on the display path

**Decision**: If the summary or fixes call fails (router stopped, unreachable, errored, or measurement
off), the tray shows an explicit "not measuring" or "measurement off" state and never blocks, retries
tightly, or crashes the popup. This mirrors the router's own fail-open posture from 004.

**Rationale**: FR-005/FR-007, SC-005, and the tray's existing tolerance of a stopped router
(`RouterController` already handles `HttpRequestException` around management calls).
