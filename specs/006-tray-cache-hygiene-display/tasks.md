# Tasks: Cache hygiene in the tray

**Feature**: `006-tray-cache-hygiene-display` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

**Organization**: By user story. US1 (routed display) is the MVP and carries the shared plumbing. US2
(fix toggle) and US3 (collect shadow figure) are near-independent workstreams that fan out after the
Phase 2 scaffold, touching mostly disjoint files. Tests are included per constitution Principle III
(comprehensive coverage at merge).

**Parallel fan-out**: `[P]` marks a task that can run concurrently with others in the same group
(different files, no incomplete dependency). Independent `[P]` groups and the three user-story
workstreams are designed to be fanned out via Claude Flow / RuFlo, not walked serially. The genuine
dependency chains are called out in the Dependencies section.

---

## Phase 1: Setup

- [X] T001 Confirm the baseline builds green before touching it: `dotnet build OmnisRouter.slnx -c Debug` (0/0). No new projects are added; this feature extends `OmnisRouter.CacheHygiene`, `OmnisRouter.Api`, `OmnisRouter.LocalProxy`, `OmnisRouter.Tray`, and `OmnisRouter.Collect`.

## Phase 2: Foundational (blocking prerequisites for the tray-facing stories)

- [X] T002 Add an empty cache-hygiene section to the popup: a container in `src/OmnisRouter.Tray/StatusPopup.cs` placed below the today line and above the "Full dashboard →" link, plus a new `src/OmnisRouter.Tray/CacheHygieneView.cs` that owns rendering the section. Renders "nothing measured yet" until fed. This scaffold is where US1/US2/US3 render.
- [X] T003 [P] Add a cache namespace to the tray's loopback client `src/OmnisRouter.LocalProxy/RouterManagementClient.cs`: method stubs `GetCacheSummaryAsync`, `GetCacheFixesAsync`, `SetCacheFixesAsync` (thrown-NotImplemented bodies filled in the story phases), so the tray compiles against the shape all three stories use.

**Checkpoint**: the popup shows an inert cache-hygiene section; the client has the cache method surface.

---

## Phase 3: User Story 1 — See what caching is costing, at a glance (Priority: P1) 🎯 MVP

**Goal**: The tray popup shows avoidable cache waste and recovered saving for routed traffic, content-free, every number explained.

**Independent Test**: Run the router under the tray, drive a CRLF miss (fix off) then a recovered miss (fix on), open the popup, confirm the waste and recovered figures appear and reconcile with the receipts.

- [X] T004 [US1] Add `src/OmnisRouter.CacheHygiene/CacheHygieneTally.cs`: a thread-safe running tally with since-start and today buckets (today resets at the local day boundary via the injected `TimeProvider`), accumulating avoidable waste £, recovered £, recomputed/saved tokens, miss/recovery counts, plus the latest `PricingStamp` and a `Snapshot()` returning an immutable projection. Per data-model.md.
- [X] T005 [US1] Record into the tally from `src/OmnisRouter.CacheHygiene/CacheHygieneService.cs`: on each non-null `CacheHygieneResult`, add to both buckets (miss when `WasteGbp > 0`, recovery when `SavedGbp > 0`). No hot-path cost beyond the increment. Depends on T004.
- [X] T006 [US1] Register `CacheHygieneTally` as a singleton and expose it to the service in `src/OmnisRouter.Api/Program.cs` (extend the existing CacheHygiene registration). Depends on T004.
- [X] T007 [US1] Add `GET /v1/analytics/cache-hygiene/summary` in `src/OmnisRouter.Api/Endpoints/CacheHygieneSummary.cs`, projecting the tally to the content-free shape in contracts/cache-hygiene-summary.md (measurement_enabled, since_start, today, pricing_version, fx_date, usd_gbp, shadow_price, source=routed). Map it in the endpoint wiring. Depends on T004, T006.
- [X] T008 [P] [US1] Implement `GetCacheSummaryAsync` in `src/OmnisRouter.LocalProxy/RouterManagementClient.cs` (deserialise the summary shape). Depends on T003.
- [X] T009 [US1] Fill `src/OmnisRouter.Tray/CacheHygieneView.cs` to render the summary: a waste line and a recovered line in GBP with period labels, the pricing/FX/shadow basis discoverable, and the "nothing measured yet" / "measurement off" / "not measuring" states. Content-free. Depends on T002, T008.
- [X] T010 [US1] Wire the popup to fetch and refresh the summary on the cadence it already updates status, in `src/OmnisRouter.Tray/StatusPopup.cs` / `TrayContext.cs`, fail-open (a failed fetch shows "not measuring", never blocks or crashes). Depends on T009.
- [X] T011 [P] [US1] Tally tests in `tests/OmnisRouter.CacheHygiene.Tests/CacheHygieneTallyTests.cs`: accumulation, avoidable-only waste, recovery counting, day-boundary reset (ManualClock), snapshot immutability. Depends on T004.
- [X] T012 [P] [US1] Summary endpoint tests in `tests/OmnisRouter.Api.Tests/CacheSummaryEndpointTests.cs`: figures reconcile with recorded results; fresh-start zeros; measurement-off state; content-free over the response body. Depends on T007.
- [X] T013 [P] [US1] Management-client test in `tests/OmnisRouter.LocalProxy.Tests/RouterManagementClientCacheTests.cs`: `GetCacheSummaryAsync` parses the shape and tolerates an error/absent router. Depends on T008.

**Checkpoint**: MVP — a self-hoster sees routed cache waste and savings in the tray, content-free.

---

## Phase 4: User Story 2 — Turn recovery on without editing a config file (Priority: P2)

**Goal**: Toggle the byte-mutating fixes from the tray at runtime; the OmnisVigil policy override still wins and the tray shows the effective state.

**Independent Test**: With US1 present, enable the line-ending fix from the tray, repeat the repeated-prefix traffic, confirm the recovered figure rises with no restart.

- [X] T014 [US2] Make the local enabled-fix set safely mutable at runtime in `src/OmnisRouter.CacheHygiene/CacheHygieneOptions.cs` / `CacheHygieneService.cs` (a volatile snapshot or lock, so `Normalise` reads a consistent set while a `PUT` swaps it). Preserve `IFixPolicy` precedence: policy still consulted first.
- [X] T015 [US2] Add `GET/PUT /v1/cache-hygiene/fixes` in `src/OmnisRouter.Api/Endpoints/CacheHygieneFixes.cs`, returning `{ local, effective, policy_overrides }` and validating PUT values against the three fix classes, per contracts/cache-fixes-control.md. Depends on T014.
- [X] T016 [P] [US2] Implement `GetCacheFixesAsync` and `SetCacheFixesAsync` in `src/OmnisRouter.LocalProxy/RouterManagementClient.cs`. Depends on T003.
- [X] T017 [US2] Add per-class fix toggles to the popup section in `src/OmnisRouter.Tray/CacheHygieneView.cs` / `StatusPopup.cs`: reflect `effective` state, and when `policy_overrides` is true show the policy is in control rather than presenting the local toggle as authoritative. A toggle calls `SetCacheFixesAsync` and re-reads. Depends on T009, T016.
- [X] T018 [P] [US2] Fixes endpoint tests in `tests/OmnisRouter.Api.Tests/CacheFixesEndpointTests.cs`: PUT changes the local set and takes effect on the next `Normalise`; policy override wins and is reported in `effective`/`policy_overrides`; unknown fix values rejected. Depends on T015.
- [X] T019 [P] [US2] Runtime-mutation test in `tests/OmnisRouter.CacheHygiene.Tests` that a swapped enabled set is read consistently by `Normalise` (no torn read). Depends on T014.

**Checkpoint**: the self-hoster enables recovery from the tray; policy precedence is shown honestly.

---

## Phase 5: User Story 3 — Cover the watched subscription (Priority: P3)

**Goal**: In collect mode, show the gross observed cache-write shadow cost, labelled an estimate, with no cause classification (collect is content-free).

**Independent Test**: Run the tray in collect mode against a prefix-reusing subscription workload; confirm the popup shows a shadow-priced observed cache-write cost labelled "estimate, not a bill".

- [X] T020 [US3] Accumulate observed cache-write cost in `src/OmnisRouter.Collect/CollectEngine.cs`: sum `cache_creation_input_tokens` per period (since-start / today) and price at the cache-write premium, shadow-priced. Content-free (token counts only). Per data-model.md "Observed cache cost".
- [X] T021 [US3] Surface the collect figure to the tray: expose it through the summary shape with `source = collect`, `shadow_price = true` (either a collect-side summary read or extending `CollectionStatus`), so the tray renders it. Depends on T020, T009.
- [X] T022 [US3] Render the collect-mode variant in `src/OmnisRouter.Tray/CacheHygieneView.cs`: gross observed cache-write shadow cost, labelled "estimate, not a bill", no cause breakdown, no avoidable/unavoidable split. Depends on T021.
- [X] T023 [P] [US3] Observed-cost tests in `tests/OmnisRouter.Collect.Tests/ObservedCacheCostTests.cs`: cache-write tokens summed and shadow-priced per period; content-free; day-boundary reset; no cause/avoidability fields present. Depends on T020.

**Checkpoint**: the subscription watcher sees an honest shadow cost for observed cache churn.

---

## Phase 6: Polish & Cross-Cutting

- [X] T024 [P] Content-free gate test over the new surfaces in `tests/OmnisRouter.Api.Tests`: the summary and fixes responses carry scalars and labels only, no prompt text, diff, or key (SC-003), mirroring 004's outbound-record test.
- [X] T025 [P] Document the tray cache-hygiene surface in `docs/collect-tray.md` and the two new endpoints in `docs/api.md` and `docs/self-host.md` (operator note): what each figure means, the fix toggle, and the collect-mode shadow caveat.
- [X] T026 Run the quickstart.md scenarios 1–6 and record the results in quickstart.md.
- [X] T027 Final gate: `dotnet build OmnisRouter.slnx -c Release` (0 error / 0 warning) and `dotnet test OmnisRouter.slnx -c Release` green.

---

## Dependencies

- **Phase 1 → Phase 2 → user stories.** Phase 2 (T002 popup scaffold, T003 client surface) blocks the tray side of all three stories.
- **US1 chain**: T004 → {T005, T006} → T007 → T008 → T009 → T010. Tests T011/T012/T013 depend only on their targets and run `[P]`.
- **US2 chain**: T014 → T015; T016 `[P]`; T017 depends on T009 (US1 view) + T016. Tests T018/T019 `[P]`.
- **US3 chain**: T020 → T021 (needs T009) → T022. Test T023 `[P]`.
- **Cross-story**: US2 and US3 both extend the tray view T009 builds, so US1's T009 lands before US2's T017 and US3's T022. The router-side work (T014–T016, T020) is independent of US1 and can run in parallel with it.

## Parallel execution / fan-out

- After Phase 2, three workstreams fan out: **A (US1)**, **B (US2 router side: T014–T016, T018–T019)**, **C (US3 collect side: T020, T023)**. Only the shared tray view (T009, then T017/T022) serialises the UI edits.
- Within US1, the endpoint (T007) and the client method (T008) run `[P]`; all three US1 test tasks run `[P]` once their targets exist.
- Tray UI edits to `StatusPopup.cs` / `CacheHygieneView.cs` touch shared files, so T009, T017, T022 are sequential with per-task review (a legitimate single-tree dependency chain, not a missed parallelism).

## Implementation strategy

- **MVP = Phase 1 + Phase 2 + US1.** Routed cache waste and savings in the tray, content-free, is shippable and valuable on its own.
- Then US2 (recovery control) and US3 (subscription shadow figure) as parallel increments.
- Ship and validate US1 before starting US2/US3.
