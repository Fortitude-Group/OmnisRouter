# Implementation Plan: Cache hygiene — a measured cost lever

**Branch**: `004-cache-hygiene` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-cache-hygiene/spec.md`; source design at `docs/superpowers/specs/2026-09-18-cache-hygiene-design.md`.

## Summary

Measure prompt-cache waste in the request path from the bytes the router already forwards, and — where a fix is provably semantics-preserving and the operator has opted in — remove the cause before forwarding so the miss becomes a cache read. Show the measured tokens and pounds saved in the receipt, and report content-free cache-waste fields on the existing receipts-up record for OmnisVigil to aggregate. The per-request byte analysis runs in the router (content-free-out forbids sending bytes anywhere), reimplemented in C# against the sibling tool's versioned result contract. Anthropic-first, where the explicit cache breakpoint makes the prefix exact. Measurement is default-on and content-free; every byte-mutating fix is opt-in per class and off by default; nothing about this ever blocks, slows, or fails a request.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`, cross-platform). Repo-wide `Nullable`, `ImplicitUsings`, `LangVersion latest`, `TreatWarningsAsErrors=true` apply to every new project.

**Primary Dependencies**: existing `OmnisRouter.Core` (`ChatRequest`/`ChatResponse`, `ModelDecision`, `DecisionLogEntry`), `OmnisRouter.Adapters.Anthropic` + `OmnisRouter.Upstream` (`AnthropicRequestMapper`, the egress wire serialisation and the `cache_control` breakpoint), `OmnisRouter.Store.Pricing` (`PricingBook`, the dated snapshot), `OmnisRouter.Vigil` (`IngestRecordMapper`), `OmnisRouter.Api` (`RoutedRequestHandler`, receipt headers, `ReceiptJson`, `AnalyticsDecisions`). New: `System.Text.Json` for canonical tool-JSON ordering. xunit for tests.

**Storage**: no new persistent store for analysis. A bounded **in-memory** lineage cache holds the previous prefix representation only. The dated pricing snapshot yaml (`config/pricing/*.yaml`) gains `usd_gbp` + `fx_date`. `DecisionLogEntry` gains cache-waste columns, so the SQLite and Npgsql stores each need a migration (precedent: the `AttributionTags` migration).

**Testing**: xunit. New `tests/OmnisRouter.CacheHygiene.Tests` (analyzer conformance vs the sibling tool's vectors, cause classification, normaliser response-transparency, lineage cache, pricing/FX). Additions to `OmnisRouter.Api.Tests` (content-free over outbound records, the receipt cache block, fail-open fault injection) and the ingest-mapper tests.

**Target Platform**: the router process (Linux container, Windows, macOS) — cross-platform `net10.0`. No platform-specific code.

**Project Type**: shared library (`OmnisRouter.CacheHygiene`) + service wiring (`OmnisRouter.Api`) + contract/pricing/ingest changes.

**Performance Goals**: no measurable added latency on the request hot path (SC-003); analysis runs off the hot path after the response; any in-path normalisation stays within a small stated budget.

**Constraints**: content-free-out (no prompt bytes, diff, or keys leave the machine — FR-005/FR-014); fail-open (never block, delay past budget, or fail a request — FR-019); measured, not predicted (real `Usage` + real breakpoints — FR-003); mutations opt-in, measurement default-on (FR-005/FR-009); additive (no change to routing decisions or wire translation — FR-022).

**Scale/Scope**: per-request, single router process. Anthropic-routed requests only in this release. The lineage cache is bounded by size and age.

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1. Constitution v1.5.0.*

| Principle | Assessment | Verdict |
|---|---|---|
| I. Modular & Composable | One new library, `OmnisRouter.CacheHygiene`, single purpose (analyse + normalise + the result contract), consumed by Api/pricing/Vigil as a thin dependency. | PASS |
| II. Contract Stability & SemVer | Additive: a new optional `cache_waste` block on the ingest record and a `cache` block on the routing receipt. Both are versioned in their schemas with a migration note; no existing field changes. The ingest change carries a cross-repo compatibility constraint (below). | PASS (additive, noted) |
| III. Comprehensive Tests | Content-free test over outbound records, response-transparency, fail-open fault injection, analyzer conformance vs the sibling tool's vectors, per-`CauseClass`, pricing/FX. | PASS |
| IV. Deterministic & Observable | The analyzer is pure given (prefixes, usage, pricing, clock); FX comes from the dated snapshot; the receipt and decision log make every figure traceable. | PASS |
| V. Simplicity & Justified Complexity | Reuses the request path, receipt, pricing snapshot, and receipts-up path. The one non-trivial addition — a C# reimplementation of the sibling cost core — is justified below (content-free-out forbids the alternative). | PASS |
| VI. Complete the Scope | Anthropic-first is a deliberate, recorded scope boundary (OpenAI/Gemini = a later feature; Gemini also needs its cache usage surfaced first). No in-scope work deferred. | PASS |
| VII. Tracker Is the Project of Record | OmnisRouter has no external board (GitHub; `specs/` + git history are the record). | PASS (n/a board) |
| VIII. Start From a Fresh Base | Implementation starts from a freshly pulled `main` (003 is tied off; working tree clean). | PASS (enforced at implement) |
| IX. Ask, Then Wait | All gating decisions (Anthropic-first, opt-in, cost-core location, retention, FX) were locked with the owner in brainstorming. | PASS |
| X. Production Changes Wait for a Human | No production apply here. The one production-adjacent risk is the ingest contract: an old OmnisVigil rejecting a new `cache_waste` block. Mitigated by emission gating + deploy ordering (research R5); the router does not emit the block into prod until Vigil accepts it. | PASS (mitigated) |
| XI. Establish the Mechanism | Plan grounded in a fresh survey of the actual request path, receipt, ingest mapper, adapters, pricing, and the sibling tool (file anchors in research.md). | PASS |
| XII. Explain Every Number | Every figure answers what/why/what-follows: avoidable waste = the fixable money (with its cause and fix), saved = recovered, unavoidable is not priced. The reframe that removed the non-actionable total-of-all-misses already applied this. | PASS |

**Gate result: PASS**, with one justified complexity entry (the C# cost-core reimplementation) recorded below, and two carry-forward notes: the ingest-contract cross-repo ordering (research R5) and the `DecisionLogEntry` migration.

## Project Structure

### Documentation (this feature)

```text
specs/004-cache-hygiene/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── cache-waste-ingest.md   # the frozen content-free block on the ingest record (the hand-off)
│   ├── receipt-cache-block.md  # X-Omnis-Cache-* headers + the /v1/route cache section
│   ├── analyzer.md             # CacheHygieneAnalyzer I/O + reconciliation to the sibling result contract
│   └── normalizers.md          # the three fix classes, their safety proofs, and their config
└── tasks.md             # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/
├── OmnisRouter.CacheHygiene/          # NEW net10.0 library
│   ├── CacheHygieneAnalyzer.cs        #   prefixes + usage + pricing -> result (pure, deterministic)
│   ├── CacheHygieneResult.cs          #   C# port of the sibling RESULT contract; CauseClass enum
│   ├── LineageCache.cs                #   bounded in-memory last-prefix-per-lineage, size/age evicted
│   ├── PrefixExtractor.cs             #   the wire prefix up to and including the Anthropic breakpoint
│   ├── Normalizers/                   #   LineEndingNormalizer, TrailingWhitespaceNormalizer, ToolOrderingNormalizer
│   ├── CacheHygieneOptions.cs         #   per-class enable flags (off by default), budgets
│   └── PricingStamp.cs                #   pricing_version + fx_date + usd_gbp + measured/shadow
├── OmnisRouter.Store/Pricing/         #   add usd_gbp + fx_date to the snapshot + PricingBook.EstimateGbp/Stamp
├── OmnisRouter.Core/                  #   ModelDecision gains a Cache block; DecisionLogEntry gains cache fields
├── OmnisRouter.Api/
│   ├── Endpoints/RoutedRequestHandler.cs  # pre-forward normalise hook + off-path analysis hook
│   ├── Endpoints/AnalyticsDecisions.cs    # mirror the new fields (second serialiser)
│   ├── Routing/ReceiptJson.cs             # cache section in /v1/route
│   └── (WriteReceiptHeaders)               # X-Omnis-Cache-* headers
├── OmnisRouter.Vigil/IngestRecordMapper.cs # the content-free cache_waste block
└── OmnisRouter.Store.Migrations.{Sqlite,Npgsql}/  # DecisionLogEntry cache columns

config/pricing/*.yaml                  # gains fx: { usd_gbp, fx_date }
docs/omnisvigil-integration-contract.md
docs/contracts/omnisvigil-ingest-record.schema.json
docs/contracts/routing-receipt.schema.json

tests/
├── OmnisRouter.CacheHygiene.Tests/    # NEW — analyzer, cause classes, normalizers, lineage, pricing/FX
├── OmnisRouter.Api.Tests/             # content-free over outbound records, receipt cache block, fail-open
└── (ingest mapper tests)              # cache_waste emission + content-free
```

**Structure Decision**: a new cross-platform `OmnisRouter.CacheHygiene` library holds the analysis, the normalisers, the lineage cache, and the C# port of the sibling result contract, added to `OmnisRouter.slnx`. It is consumed by `OmnisRouter.Api` (the request-path hooks and the receipt), `OmnisRouter.Store.Pricing` (FX + the pound stamp), `OmnisRouter.Vigil` (the content-free block), and `OmnisRouter.Core` (the `ModelDecision`/`DecisionLogEntry` fields). Freezing the content-free `cache_waste` contract (schema + doc) is a first-class deliverable (FR-013).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| A C# reimplementation of the sibling tool's cache cost core (logic exists in Python already) | The per-request byte analysis must run in the router, because content-free-out forbids prompt bytes leaving the machine; the router is C#. The two implementations are kept in agreement by sharing the sibling tool's versioned RESULT/CAPTURE contract and porting its test vectors as a conformance suite. | Calling the existing Python cost core would require sending prompt bytes off the machine to the analyser, which violates the locked content-free guarantee — the whole reason the feature can exist on a self-hosted router talking to a hosted control plane. |
