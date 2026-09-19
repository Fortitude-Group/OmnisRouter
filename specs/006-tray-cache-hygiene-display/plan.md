# Implementation Plan: Cache hygiene in the tray

**Branch**: `006-tray-cache-hygiene-display` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-tray-cache-hygiene-display/spec.md`

## Summary

Surface feature 004's cache-hygiene in the Windows tray. The router keeps a small in-memory running
tally of cache waste and recovered saving as it already computes each request's `CacheHygieneResult`,
and exposes it content-free on a new lightweight summary endpoint. The tray polls that endpoint over its
existing loopback management client and adds a cache-hygiene section to the status popup, every figure
labelled with its unit, period, pricing/FX basis and bill-versus-shadow marking. A second management
endpoint lets the tray toggle the byte-mutating fixes at runtime, with an OmnisVigil policy override
still winning where present. Collect mode gets a coarser, honest figure: it already reads
cache-creation and cache-read token counts per transcript entry, so it can show the shadow cost of
observed cache re-writes, but it cannot classify cause or avoidability because the transcript reader is
content-free and never reads prefix bytes.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`; the tray targets `net10.0-windows`)

**Primary Dependencies**: WinForms (tray popup, `StatusPopup`), ASP.NET Core minimal API (router
endpoints), existing `OmnisRouter.CacheHygiene` (analyzer, result, options, `IFixPolicy`),
`OmnisRouter.LocalProxy` (`RouterManagementClient`, the tray's loopback client), `OmnisRouter.Collect`
(`CollectEngine`, `UsageEntry`, `CollectionStatus`)

**Storage**: none new. The router holds a small in-memory running tally (a few scalars, reset on
restart); collect-mode figures derive from the token counts the engine already reads. No schema change,
no migration.

**Testing**: xUnit across the existing `*.Tests` projects (`OmnisRouter.CacheHygiene.Tests`,
`OmnisRouter.Api.Tests`, `OmnisRouter.LocalProxy.Tests`, `OmnisRouter.Collect.Tests`)

**Target Platform**: Windows 10/11 (the tray). The router endpoints are cross-platform but this
feature's user surface is the Windows tray.

**Project Type**: desktop-app (tray) supervising a local web-service (router)

**Performance Goals**: popup shows figures within 5 seconds of opening (SC-001); the summary endpoint is
O(1) from the running tally, so no per-request query; zero added hot-path latency (the tally updates
from the `CacheHygieneResult` the routed path already produces)

**Constraints**: content-free (no prompt bytes, diff, or key on any surface); fail-open (the popup and
tray never block, hang, or crash if figures cannot be obtained); every number explained (constitution
Principle XII)

**Scale/Scope**: single-user desktop; the running tally is a handful of scalars; the tray polls at the
same cadence it already refreshes status

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Modular & Composable** — PASS. Aggregation is a small addition inside `OmnisRouter.CacheHygiene`,
  exposed by one Api endpoint and consumed by the tray through the existing `RouterManagementClient`.
  The collect-mode figure reuses `CollectEngine`'s existing token reads. No new cross-cutting coupling.
- **II. Contract Stability & SemVer** — PASS. Two new router routes and two new management-client
  methods are purely additive (a MINOR change). The OmnisVigil ingest and policy contracts are
  untouched. The tray-to-router surface is loopback and versioned with the bundled router.
- **III. Comprehensive Tests for Public Contracts** — PASS. Planned: aggregator unit tests, summary and
  fixes endpoint tests, the collect coarse-figure test, and tray rendering/format tests, all at merge.
- **IV. Deterministic & Observable** — PASS. Figures are deterministic given the inputs and the pinned
  pricing snapshot; the surface is content-free and self-describing.
- **V. Simplicity & Justified Complexity** — PASS. In-memory running counters plus one read endpoint and
  one write endpoint. No new storage, no new dependency, no background worker.
- **VI. Complete the Scope** — PASS. All three user stories are planned. The collect-mode limitation
  (coarse observed figure, no cause classification) is a documented consequence of the content-free
  transcript reader, not deferred work.
- **X. Production Changes Wait for a Human** — N/A. This is a local desktop/dev surface; it touches no
  production environment.
- **XI. Establish the Mechanism** — PASS. The plan is built from the verified code: the tray's popup
  data flow (`StatusPopup` fed `CollectionStatus`/`RouterStatus`), the loopback `RouterManagementClient`,
  004's `CacheHygieneResult` on the routed path, and the content-free `UsageEntry` that bounds US3.
- **XII. Explain Every Number** — PASS. Central to the design: every figure carries unit, period,
  pricing version, FX date, and a bill-versus-shadow marker.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/006-tray-cache-hygiene-display/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── cache-hygiene-summary.md
│   ├── cache-fixes-control.md
│   └── tray-popup.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── OmnisRouter.CacheHygiene/
│   ├── CacheHygieneTally.cs        # NEW: thread-safe in-memory running totals + a Snapshot
│   └── CacheHygieneService.cs      # updated: record each result into the tally
├── OmnisRouter.Api/
│   └── Endpoints/
│       ├── CacheHygieneSummary.cs  # NEW: GET /v1/analytics/cache-hygiene/summary
│       └── CacheHygieneFixes.cs    # NEW: GET/PUT /v1/cache-hygiene/fixes (effective + local state)
├── OmnisRouter.LocalProxy/
│   └── RouterManagementClient.cs   # updated: GetCacheSummaryAsync, Get/SetCacheFixesAsync
├── OmnisRouter.Tray/
│   ├── StatusPopup.cs              # updated: cache-hygiene section + fix toggles
│   └── CacheHygieneView.cs         # NEW: formats the summary + fix state for the popup
└── OmnisRouter.Collect/
    └── CollectEngine.cs           # updated: accumulate observed cache-write shadow cost

tests/
├── OmnisRouter.CacheHygiene.Tests/CacheHygieneTallyTests.cs
├── OmnisRouter.Api.Tests/CacheSummaryEndpointTests.cs, CacheFixesEndpointTests.cs
├── OmnisRouter.LocalProxy.Tests/RouterManagementClientCacheTests.cs
├── OmnisRouter.Tray.Tests (or CacheHygieneView tests in an existing tray-testable project)
└── OmnisRouter.Collect.Tests/ObservedCacheCostTests.cs
```

**Structure Decision**: Extend the existing projects rather than add new ones. The router (`Api` +
`CacheHygiene`) owns the numbers, the tray (`Tray` + `LocalProxy` client) renders and controls them, and
collect (`Collect`) contributes the coarse observed figure. This keeps the split the codebase already
has (router computes, tray displays) and adds no new assembly.

## Complexity Tracking

No constitution violations. This section is intentionally empty.
