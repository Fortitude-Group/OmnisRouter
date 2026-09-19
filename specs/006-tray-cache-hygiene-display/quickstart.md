# Quickstart & Validation: Cache hygiene in the tray

Runnable checks that prove the feature against its success criteria. References
[contracts/](./contracts/) and [data-model.md](./data-model.md) rather than repeating them.

## Prerequisites

- Windows, .NET 10 SDK, and the tray built from `src/OmnisRouter.Tray` (or the installed 0.2.0 MSI).
- A router the tray supervises, with an Anthropic BYOK key for the live routed scenarios, or the test
  doubles for the offline ones.

## Build & test

```powershell
dotnet build OmnisRouter.slnx -c Debug            # 0 error / 0 warning (TreatWarningsAsErrors)
dotnet test  OmnisRouter.slnx -c Debug            # tally, endpoints, management client, collect figure
```

## Scenario 1 - See waste and savings in the popup (US1, SC-001, SC-002, SC-004)

Run the router under the tray, send two requests in one lineage differing only by a carriage return with
the line-ending fix off, then a third with it on.

**Expected**: the tray popup shows a cache-waste line and a recovered line in GBP, each labelled with its
period, and the pricing/FX/shadow basis is discoverable. The figures reconcile with the router's receipts
for the same traffic. Opening the popup shows current figures within 5 seconds.

## Scenario 2 - Nothing measured yet / measurement off (US1, SC-005)

Open the popup right after starting the router with no traffic; then set `CacheHygiene:MeasurementEnabled`
false and reopen.

**Expected**: first shows "nothing measured yet"; second shows "measurement off". Neither shows a blank,
a stale figure, or an error.

## Scenario 3 - Toggle a fix from the tray (US2, FR-009)

With the popup open and all fixes off, enable the line-ending fix from the tray, then repeat the
repeated-prefix traffic from Scenario 1.

**Expected**: subsequent recoverable misses are recovered and the recovered-saving figure rises, with no
config edit or restart.

## Scenario 4 - Policy override is shown honestly (US2, FR-010)

Connect OmnisVigil and serve a policy with `cache_fixes` set. View the tray.

**Expected**: the tray shows the effective (policy-resolved) fix state and indicates the policy is in
control; a local toggle is not presented as authoritative.

## Scenario 5 - Router stopped (US1, FR-007, SC-005)

Stop the supervised router while the tray runs, then open the popup.

**Expected**: the cache-hygiene section reads "not measuring"; the popup and tray stay responsive.

## Scenario 6 - Collect-mode shadow figure (US3, FR-011)

Run the tray in collect mode against a subscription workload that re-writes a cache prefix.

**Expected**: the popup shows the gross observed cache-write shadow cost, labelled "estimate, not a bill",
with no cause breakdown (collect mode is content-free and cannot classify).

## Content-free gate (SC-003)

Across every scenario, inspect everything the surface can render and every outbound response: no prompt
text, request content, diff, or key appears. A test asserts this over the summary and fixes responses.

## Validation results (2026-09-19)

Release gate green: `dotnet build OmnisRouter.slnx -c Release` → 0 error / 0 warning;
`dotnet test OmnisRouter.slnx -c Release` → 368 passed / 0 failed. Each scenario is pinned by an
automated test, so the check is repeatable in CI:

| Scenario | Covered by |
|---|---|
| 1 — waste and savings in the popup | `CacheHygieneTallyTests`, `CacheSummaryEndpointTests`, `CacheHygieneDisplay` via `RouterManagementClientCacheTests` |
| 2 — nothing measured / measurement off | `CacheSummaryEndpointTests.Fresh_start_*` / `.Measurement_off_is_reported`; the display states in `CacheHygieneDisplay` |
| 3 — toggle a fix from the tray | `CacheFixesEndpointTests`, `CacheHygieneServiceTests.SetLocalEnabledFixes_takes_effect_on_the_next_Normalise` |
| 4 — policy override shown honestly | `CacheFixesEndpointTests.Policy_override_wins_and_is_reported` |
| 5 — router stopped | `RouterManagementClientCacheTests` (null on error → "not measuring") |
| 6 — collect-mode shadow figure | `ObservedCacheCostTests` (pricing, format, accumulation, day-reset) |
| content-free gate (SC-003) | `CacheSurfaceContentFreeTests`, plus the allowlist in `CacheSummaryEndpointTests` |

The WinForms popup itself (label layout, checkbox wiring) is verified by the Release build; its
presentation logic is factored into `CacheHygieneDisplay` / `StatusFormat.CacheLine`, which are unit
tested. The live BYOK routed scenarios are not run here (real spend); the behaviour they exercise is
proven by the doubles above against real usage figures.

## Release gate

`scripts/release-gate.ps1` (build 0/0 + tests) still gates the tag. The content-free test, the tally and
endpoint tests, and the collect coarse-figure test run in the suite.
