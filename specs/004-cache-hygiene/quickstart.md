# Quickstart & Validation: Cache hygiene

Runnable checks that prove the feature against its success criteria. References
[contracts/](./contracts/) and [data-model.md](./data-model.md) rather than repeating them.

## Prerequisites

- .NET 10 SDK. A running router with an Anthropic BYOK key (for the live scenarios), or the test
  doubles for the offline ones.
- The sibling tool's result-contract test vectors, ported into `tests/OmnisRouter.CacheHygiene.Tests`.

## Build & test

```powershell
dotnet build OmnisRouter.slnx -c Debug            # 0 error / 0 warning (TreatWarningsAsErrors)
dotnet test  OmnisRouter.slnx -c Debug            # analyzer, normalizers, content-free, fail-open all green
```

## Scenario 1 — Measure a CRLF miss (US1, SC-002)

Send two requests in one lineage whose cached prefix differs only by a carriage return.

**Expected**: the second receipt (`/v1/route` cache section and `X-Omnis-Cache-*` headers) reports
`cause = crlf_drift`, `avoidable = true`, a non-zero `recomputed_tokens` and `waste_gbp` computed from
the real usage. The outbound ingest record carries the content-free `cache_waste` block and **no** bytes
— asserted by the content-free test over outbound records.

## Scenario 2 — Recover it with a fix (US3, SC-001, SC-004)

Enable `line_ending`. Repeat scenario 1.

**Expected**: the provider reports a cache **read** (not a write); the receipt shows `fix_applied =
line_ending`, `saved_tokens`/`saved_gbp` > 0, and a `before_after`. A response-transparency test confirms
the normalised and un-normalised requests return equivalent responses.

## Scenario 3 — Unavoidable miss is not priced (US1)

Send a request with a genuine content edit in the prefix.

**Expected**: `cause` is `genuine_edit` (or `model_change` / `system_prompt_change`), `avoidable = false`,
`waste_gbp = 0`. The reporting side can sum `waste_gbp` safely as avoidable-only.

## Scenario 4 — Fail-open under fault (US2, SC-003)

Inject a fault so the analyzer and a normaliser throw; drive traffic.

**Expected**: every request is served with a correct response, zero added failures, and no measurable
added hot-path latency (compare with the feature off). Records for the faulted requests simply carry no
`cache_waste` block.

## Scenario 5 — Trust the pounds (US5, SC-005)

Compare a pay-as-you-go request and a subscription request.

**Expected**: both figures carry `pricing_version` + `fx_date` + `usd_gbp`; the PAYG figure is measured,
the subscription figure has `shadow_price = true` and is labelled an estimate, never a bill.

## Scenario 6 — Off by default, additive (SC-006, SC-007)

With no configuration, drive traffic.

**Expected**: measurement runs (records carry `cache_waste`), but no request bytes are mutated (every
fix off). A request with analysis disabled, or a non-cacheable request, produces an outbound record
identical to today's.

## Release gate

`scripts/release-gate.ps1` (build 0/0 + tests + benchmark) still gates the tag. The content-free test,
the response-transparency tests, and the fail-open fault-injection tests run in the suite.

## Cross-repo note

Do not enable `cache_waste` emission into production until the OmnisVigil ingest schema has accepted the
block (research D5). The contract here is the frozen source of truth the OmnisVigil `003` spec pins to.
