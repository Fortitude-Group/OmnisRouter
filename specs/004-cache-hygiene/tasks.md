---
description: "Task list for cache hygiene — a measured cost lever"
---

# Tasks: Cache hygiene — a measured cost lever

**Input**: Design documents from `specs/004-cache-hygiene/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — the success criteria are test-shaped (content-free, response-transparency, fail-open, analyzer conformance). Per constitution III the gate is coverage at merge; tests sit beside their implementation.

**Organization**: Grouped by user story. The `OmnisRouter.CacheHygiene` library + FX are the blocking root (Phase 2); the P1 stories (measure + fail-open) are the MVP; US3/US4/US5 touch mostly disjoint files and fan out after.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: different files, no dependency on an incomplete task
- **[Story]**: US1–US5 from spec.md

## Path Conventions

New: `src/OmnisRouter.CacheHygiene/` (net10.0 lib), `tests/OmnisRouter.CacheHygiene.Tests/`. Existing touched: `src/OmnisRouter.Api/`, `src/OmnisRouter.Core/`, `src/OmnisRouter.Store/Pricing/`, `src/OmnisRouter.Vigil/`, `src/OmnisRouter.Store.Migrations.{Sqlite,Npgsql}/`, `config/pricing/`, `docs/contracts/`, `docs/`.

---

## Phase 1: Setup

- [ ] T001 Create `src/OmnisRouter.CacheHygiene/OmnisRouter.CacheHygiene.csproj` (net10.0 class library; references `OmnisRouter.Core` and `OmnisRouter.Store` for pricing) and add it to `OmnisRouter.slnx`.
- [ ] T002 [P] Create `tests/OmnisRouter.CacheHygiene.Tests/OmnisRouter.CacheHygiene.Tests.csproj` (xunit, Microsoft.NET.Test.Sdk, coverlet), reference `OmnisRouter.CacheHygiene`, add to `OmnisRouter.slnx`.
- [ ] T003 [P] Port the sibling tool's RESULT/CAPTURE contract test vectors from `ProseWeightVisualizer/src/proseweight/cache/core/` into `tests/OmnisRouter.CacheHygiene.Tests/vectors/` as fixtures for the conformance suite.

**Checkpoint**: `dotnet build OmnisRouter.slnx` succeeds with the empty new projects (0 warnings — `TreatWarningsAsErrors`).

---

## Phase 2: Foundational — the CacheHygiene core + FX (BLOCKING)

**Purpose**: The pure analyzer, its contract, the lineage cache, options, and the GBP/FX pricing path every £ needs. Every story depends on this.

**⚠️ CRITICAL**: No user story work begins until this phase and its conformance tests pass.

- [ ] T004 [P] `CauseClass` enum (the 9 values with their avoidable mapping, research D7) in `src/OmnisRouter.CacheHygiene/CauseClass.cs`.
- [ ] T005 [P] `PricingStamp` (`PricingVersion`, `FxDate`, `UsdGbp`, `ShadowPrice`) in `src/OmnisRouter.CacheHygiene/PricingStamp.cs`.
- [ ] T006 `CacheHygieneResult` record — the C# port of the sibling RESULT contract, fields per data-model.md — in `src/OmnisRouter.CacheHygiene/CacheHygieneResult.cs`.
- [ ] T007 Add `usd_gbp` + `fx_date` to the pricing snapshot yaml (`config/pricing/2026-08-15.yaml`) and to the snapshot loader/`PricingEntry` in `src/OmnisRouter.Store/Pricing/PricingBook.cs`.
- [ ] T008 GBP path on `PricingBook`: price cache write vs read, convert USD→GBP by the snapshot rate, and emit a `PricingStamp`, in `src/OmnisRouter.Store/Pricing/PricingBook.cs`. Depends on T007.
- [ ] T009 [P] `CacheHygieneOptions` (measurement default-on; per-`FixClass` flags default-off; in-path budget; lineage size/age bounds) in `src/OmnisRouter.CacheHygiene/CacheHygieneOptions.cs`.
- [ ] T010 `PrefixExtractor` — the wire prefix up to and including the Anthropic `cache_control` breakpoint, from the egress mapper output — in `src/OmnisRouter.CacheHygiene/PrefixExtractor.cs`.
- [ ] T011 [P] `LineageCache` (bounded, LRU + age evicted, in-memory, thread-safe, never persisted) in `src/OmnisRouter.CacheHygiene/LineageCache.cs`.
- [ ] T012 `CacheHygieneAnalyzer.Analyse` — divergence offset, cause classification, recomputed tokens from real `Usage`, `waste_gbp` avoidable-only, saving when the normalised prefix matches, `PricingStamp` — in `src/OmnisRouter.CacheHygiene/CacheHygieneAnalyzer.cs`. Depends on T004–T008, T010.
- [ ] T013 [P] Analyzer conformance test against the ported vectors in `tests/OmnisRouter.CacheHygiene.Tests/AnalyzerConformanceTests.cs`.
- [ ] T014 [P] Cause-classification tests, one per `CauseClass` (CRLF, trailing ws, volatile header, timestamp, tool churn, concat order, genuine edit, model/system change) in `tests/OmnisRouter.CacheHygiene.Tests/CauseClassTests.cs`.
- [ ] T015 [P] `LineageCache` eviction + thread-safety tests in `tests/OmnisRouter.CacheHygiene.Tests/LineageCacheTests.cs`.
- [ ] T016 [P] Pricing/FX tests: £ = USD × rate, cache write premium, stamp carries `pricing_version` + `fx_date`, in `tests/OmnisRouter.CacheHygiene.Tests/PricingFxTests.cs`.

**Checkpoint**: the analyzer conforms to the shared contract and prices in stamped GBP. Surfaces can now be built.

---

## Phase 3: User Story 1 — See what cache misses are costing you (Priority: P1) 🎯 MVP

**Goal**: Measurement runs off the hot path by default and shows the cause, recomputed tokens, and avoidable pounds in the receipt, content-free.

**Independent Test**: Two requests in one lineage differing only by a carriage return — the second receipt shows a `crlf_drift` cause, recomputed tokens and avoidable £ from real usage; no bytes leave the machine.

- [ ] T017 [US1] `ModelDecision.Cache` block property in `src/OmnisRouter.Core/Routing/ModelDecision.cs`.
- [ ] T018 [US1] Bounded, drop-on-full off-path analysis runner (guarantees the analysis never blocks the response) in `src/OmnisRouter.CacheHygiene/OffPathAnalysisRunner.cs`.
- [ ] T019 [US1] Wire `src/OmnisRouter.Api/Endpoints/RoutedRequestHandler.cs` (`CaptureAndLog`/`BuildLogEntry`): compute the current prefix via `PrefixExtractor`, fetch the previous from `LineageCache`, enqueue `Analyse` with the real `Usage`, store the current prefix, and build `ModelDecision.Cache` from the result. Depends on T012, T017, T018.
- [ ] T020 [US1] `X-Omnis-Cache-*` response headers in `RoutedRequestHandler.WriteReceiptHeaders` per contracts/receipt-cache-block.md. Depends on T017.
- [ ] T021 [US1] `/v1/route` cache section (incl. the caller-owned `before_after`) in `src/OmnisRouter.Api/Routing/ReceiptJson.cs`, and update `docs/contracts/routing-receipt.schema.json`. Depends on T017.
- [ ] T022 [US1] Register the CacheHygiene services + options (measurement default-on) in the Api DI wiring (`Program.cs` / an `AddOmnisCacheHygiene` extension).
- [ ] T023 [P] [US1] Receipt cache-block test — a CRLF miss shows cause/recomputed/waste in the headers and `/v1/route`; `before_after` appears only in `/v1/route`, never onward — in `tests/OmnisRouter.Api.Tests/CacheReceiptTests.cs`.

**Checkpoint**: MVP measurement — the operator sees the waste per request, content-free.

---

## Phase 4: User Story 2 — Never let this cost a request (Priority: P1)

**Goal**: The fail-open invariant that makes measurement safe to leave on.

**Independent Test**: Fault-inject the analyzer and a normaliser so each throws; every request is still served with a correct response, zero added failures, no measurable added hot-path latency.

- [ ] T024 [US2] Fail-open boundary around the normalisation and analysis hooks in `src/OmnisRouter.Api/Endpoints/RoutedRequestHandler.cs` — swallow any exception and serve the request; the off-path runner drops rather than blocks when full. Depends on T019.
- [ ] T025 [P] [US2] Fault-injection tests: analyzer/normaliser throws → request served, no cache fields, no added failure, in `tests/OmnisRouter.Api.Tests/CacheFailOpenTests.cs`.
- [ ] T026 [P] [US2] Off-path drop-on-full + no-hot-path-latency test (the response returns before/independent of the analysis) in `tests/OmnisRouter.Api.Tests/CacheHotPathTests.cs`.

**Checkpoint**: MVP (US1 + US2) — measurement that can never cost a request.

---

## Phase 5: User Story 3 — Recover the waste automatically (Priority: P2)

**Goal**: Opt-in fixes that remove a fixable cause before forwarding, turning a write into a read.

**Independent Test**: With `line_ending` enabled, a CRLF-only difference now returns a cache read and the receipt shows the saving; the response is equivalent to the un-normalised one.

- [ ] T027 [P] [US3] `INormalizer` + `LineEndingNormalizer` in `src/OmnisRouter.CacheHygiene/Normalizers/LineEndingNormalizer.cs` (contracts/normalizers.md).
- [ ] T028 [P] [US3] `TrailingWhitespaceNormalizer` in `src/OmnisRouter.CacheHygiene/Normalizers/TrailingWhitespaceNormalizer.cs`.
- [ ] T029 [P] [US3] `ToolOrderingNormalizer` (set-safe only, canonical tool-JSON key order) in `src/OmnisRouter.CacheHygiene/Normalizers/ToolOrderingNormalizer.cs`.
- [ ] T030 [US3] Apply enabled normalisers before dispatch in `RoutedRequestHandler.cs` (capture before/after, within budget, skip-on-unsafe). Depends on T027–T029, T019.
- [ ] T031 [US3] Feed the normalised prefix to `Analyse` so a fix's `saved_tokens`/`saved_gbp` are computed, and surface `fix_applied` + saved + before/after in the receipt. Depends on T012, T030.
- [ ] T032 [US3] Let an OmnisVigil policy toggle fix classes, extending the policy that already carries caps/kill, in `src/OmnisRouter.Vigil/VigilPolicy.cs` and its Api wiring.
- [ ] T033 [P] [US3] Response-transparency tests per normaliser (same request with/without the fix → equivalent response, SC-004) in `tests/OmnisRouter.CacheHygiene.Tests/NormalizerTransparencyTests.cs`.
- [ ] T034 [P] [US3] Fix behaviour tests: turns write→read + saving shown; off by default; skipped when safety can't be shown — in `tests/OmnisRouter.Api.Tests/CacheFixTests.cs`.

**Checkpoint**: the money-saver — enabled fixes recover measured waste.

---

## Phase 6: User Story 4 — Report the saving, content-free (Priority: P2)

**Goal**: The content-free `cache_waste` block rides the receipts-up record; the contract is frozen for OmnisVigil.

**Independent Test**: Outbound records carry the content-free block and a content-free test finds no bytes/diff/keys; a record with no analysis is identical to today's.

- [ ] T035 [US4] Cache-waste columns on `DecisionLogEntry` in `src/OmnisRouter.Core/Routing/DecisionLog.cs`.
- [ ] T036 [US4] SQLite migration for the new columns in `src/OmnisRouter.Store.Migrations.Sqlite/` (mirror the `AttributionTags` migration). Depends on T035.
- [ ] T037 [US4] Npgsql migration for the new columns in `src/OmnisRouter.Store.Migrations.Npgsql/`. Depends on T035.
- [ ] T038 [US4] Emit the content-free `cache_waste` block in `src/OmnisRouter.Vigil/IngestRecordMapper.cs` (closed allowlist, contracts/cache-waste-ingest.md). Depends on T035.
- [ ] T039 [US4] Mirror the fields in `src/OmnisRouter.Api/Endpoints/AnalyticsDecisions.cs` (the second serialiser). Depends on T035.
- [ ] T040 [US4] **Freeze the contract**: add `cache_waste` to `docs/contracts/omnisvigil-ingest-record.schema.json` and document it in `docs/omnisvigil-integration-contract.md` (the hand-off deliverable, FR-013).
- [ ] T041 [US4] Emission gating: emit `cache_waste` only when analysis ran and emission is enabled, so an older OmnisVigil never rejects a receipt (research D5) — in the mapper/options.
- [ ] T042 [P] [US4] Content-free test over outbound records: no prompt bytes, diff, or keys (SC-002, FR-014), in `tests/OmnisRouter.Api.Tests/CacheContentFreeTests.cs`.
- [ ] T043 [P] [US4] Optional-block test: a request with no analysis produces an outbound record identical to today's (SC-007) in `tests/OmnisRouter.Api.Tests/CacheOptionalBlockTests.cs`.

**Checkpoint**: team-level reporting flows content-free; the contract is frozen for the OmnisVigil `003` spec.

---

## Phase 7: User Story 5 — Trust the pounds (Priority: P3)

**Goal**: Every £ stamped and marked measured or shadow; never a bill on a subscription.

**Independent Test**: A PAYG request's figures are measured; a subscription request's are shadow-priced and never a bill; both carry pricing version + FX date.

- [ ] T044 [US5] Billing-model detection (pay-as-you-go vs flat-rate subscription) driving `shadow_price` on the stamp, wired through the analyzer/pricing path and both surfaces.
- [ ] T045 [P] [US5] Tests: subscription figures shadow-priced and never a bill; PAYG measured; both carry `pricing_version` + `fx_date` (SC-005) in `tests/OmnisRouter.CacheHygiene.Tests/ShadowPriceTests.cs`.

**Checkpoint**: the money framing is defensible.

---

## Phase 8: Polish & Cross-Cutting

- [ ] T046 [P] Document the receipt cache headers in `docs/api.md` and the fix-class config/policy in `docs/` (a short cache-hygiene operator note).
- [ ] T047 Run quickstart.md scenarios 1–6 and record the results.
- [ ] T048 Final gate: `dotnet build OmnisRouter.slnx -c Release` (0 error / 0 warning) and `dotnet test OmnisRouter.slnx -c Release` green.

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (core + FX, BLOCKING)** → user stories.
- **US1 (P1) + US2 (P1) are the MVP** and land together: US2 hardens US1's request-path hooks with the fail-open guarantee.
- **US3, US4, US5** build on the analysis/receipt from US1 but touch mostly disjoint files — normalisers + request-handler pre-dispatch (US3), Core/DecisionLogEntry + migrations + mapper + schema (US4), billing detection (US5) — so they fan out after the MVP.
- **Cross-repo gate:** T040 (freeze contract) + T041 (emission gating) must land before `cache_waste` is emitted into production, so an older OmnisVigil never rejects a receipt. The OmnisVigil `003` spec pins to the frozen contract.

## Parallel Opportunities

- Setup: T002, T003 after T001.
- Foundational: T004, T005, T009, T011 together; then the tests T013–T016 together.
- After the MVP (US1+US2): run US3 (T027–T034), US4 (T035–T043), US5 (T044–T045) as three parallel workstreams.

### Parallel example — after the MVP lands

```text
Workstream A (US3): T027, T028, T029 → T030 → T031 → T032 → T033, T034
Workstream B (US4): T035 → {T036, T037, T038, T039} → T040 → T041 → T042, T043
Workstream C (US5): T044 → T045
```

## Implementation Strategy

- **MVP** = Phase 1 + Phase 2 + US1 + US2: content-free measurement in the receipt that can never cost a request. Ship and validate here — measurement-only is worth having with zero mutation.
- **Increment 2** = US3 (the fixes) + US4 (content-free reporting + the frozen contract).
- **Increment 3** = US5 (the pound stamping/shadow framing).

## Notes

- `[P]` = different files, no incomplete dependency.
- Constitution III: coverage is the merge gate; tests sit beside their implementation.
- Every pound figure carries its `PricingStamp`; nothing content-bearing leaves the machine (the content-free test is the tripwire).
- Do not enable `cache_waste` emission in production until the OmnisVigil ingest schema accepts it (T040/T041).
