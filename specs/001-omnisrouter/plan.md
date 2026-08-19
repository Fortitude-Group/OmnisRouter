# Implementation Plan: OmnisRouter v1 — Transparent LLM Routing Proxy

**Branch**: `001-omnisrouter` | **Date**: 2026-08-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-omnisrouter/spec.md`; approved design spec `docs/superpowers/specs/2026-08-18-omnisrouter-design.md`.

## Summary

OmnisRouter is a self-hosted, open-core ASP.NET Core (Kestrel, .NET 10) proxy that accepts LLM requests in Anthropic Messages, OpenAI Chat Completions, and Gemini formats, normalizes each to a neutral internal representation, routes to the **cheapest capable** model via an in-process ONNX cluster-scorer, translates to the chosen upstream's format (preserving streaming, tools, vision, caching, and thinking where a faithful mapping exists), and returns the response in the client's original format with a **routing receipt** attached. The routing model (centroids + policy table) is versioned, reproducible from public data, and stamped into every decision. Technical approach and all previously-open decisions are resolved in [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (locked by design spec — team's native stack; first-class `Microsoft.ML.OnnxRuntime`; Kestrel; single-file publish)

**Primary Dependencies**: ASP.NET Core / Kestrel (minimal APIs, .NET 10 `System.Net.ServerSentEvents` + `TypedResults.ServerSentEvents`); `Microsoft.ML.OnnxRuntime` + `Microsoft.ML.Tokenizers` (embedder `bge-small-en-v1.5` int8, fallback `gte-small`); EF Core (`Microsoft.EntityFrameworkCore.Sqlite` default, `Npgsql.EntityFrameworkCore.PostgreSQL` optional); `System.Security.Cryptography.AesGcm` (BYOK); `Microsoft.Extensions.Http.Resilience` (non-streaming calls only); OpenTelemetry (OTLP). Thin `npx` JS installer for client wiring.

**Storage**: SQLite (single-file self-host default) via EF Core; optional Postgres (Npgsql) for scale. Per-provider migrations assemblies. Provider keys stored AES-256-GCM encrypted via an EF `ValueConverter`.

**Testing**: `dotnet test` (xUnit). Golden-file adapter conformance (round-trip + cross-format), routing unit tests with a deterministic embedder stub, session-pinning multi-turn tests, BYOK/security tests (no key/prompt leakage), streaming integration through a mock upstream (back-pressure + cancellation), golden routing-model determinism test. Recorded upstream fixtures; **no live keys in CI**.

**Target Platform**: Cross-platform self-host — single-file `dotnet publish` binary + `docker compose`. Linux/Windows/macOS.

**Project Type**: Web service (single ASP.NET Core service) + offline routing-model build job + thin JS installer + static self-host dashboard. Multi-project .NET solution (`OmnisRouter.slnx`).

**Performance Goals**: ≤50ms added routing overhead p95 (in-process embed; no network hop on the critical path — FR-020, SC-003); streaming first-token latency dominated by upstream, router adds negligible buffering.

**Constraints**: Prompts never leave operator infra except to the chosen upstream (FR-013); no key/prompt plaintext in logs/receipts/exports (FR-014); single process + embedded datastore, no external dependency for core routing (FR-015); routing model reproducible from public data and version-stamped into every decision (FR-006); GCM nonce never reused; streaming client excluded from retry to avoid double-charge.

**Scale/Scope**: v1 = single-operator / small-team self-host (bottom-up developer adoption), not multi-tenant SaaS. 3 request formats, ~4 upstream providers (Anthropic/OpenAI/Gemini/OpenRouter), a candidate model pool of tens of models, k≈64 intent clusters.

**Deferred to implementation (defaults set in research.md, not blockers)**: exact temperature `T` and floor `τ` calibration values; final `k` after the elbow/silhouette sweep; installer packaging detail; dashboard chart richness (v1 = minimal spend + savings + decisions table).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.* Evaluated against `.specify/memory/constitution.md` (shared base v1.5.0).

| Principle | Status | How this plan satisfies it |
|---|---|---|
| **Prime Directive — Boil the Ocean** | PASS | Scope is the complete v1 self-host router with no known deferred-but-in-scope work; explicit v2 boundary (hosted/enterprise) documented in spec §Assumptions. |
| **I. Modular & Composable** | PASS | Solution split into focused, independently testable projects (Api/Core/Adapters/Routing/Upstream/Store/Telemetry); each provider is an isolated adapter + upstream client behind an interface. |
| **II. Contract Stability & SemVer** | PASS | Public contracts (three wire formats, `/v1/route`, receipt headers/schema, decision-log export) captured in `contracts/`; the **receipt schema is treated as a stable versioned public contract**; `policy_version` versions the routing model independently. |
| **III. Comprehensive Tests for Public Contracts** | PASS (gate at merge) | Testing strategy front-loads adapter conformance (incl. cross-format) + routing determinism + BYOK leakage + streaming; release gated on clean build + green tests **and** a passing OmnisBench run (FR-018). |
| **IV. Deterministic & Observable Behaviour** | PASS | Golden routing-model determinism test (committed centroids/policy + fixed inputs → stable decisions); receipts + decision log + OTLP make every decision observable; `policy_version` stamped per decision. |
| **V. Simplicity & Justified Complexity** | PASS | Binary confidence gate over blended scoring; filter-then-rank policy over opaque score; direct AesGcm over a heavier abstraction; SQLite single-file default. Any complexity (multi-project, per-provider migrations) justified in Complexity Tracking. |
| **VI. Complete the Scope** | PASS | Every FR maps to planned work; no silent truncation — capability guardrails return explicit errors rather than dropping features. |
| **VII. Tracker Is the Project of Record** | PASS | Spec Kit artifacts (spec/plan/research/contracts/tasks) are the tracked record; progress reflected in program memory. |
| **VIII. Start From a Fresh Base** | PASS | New repo, git initialized, clean `main`; work proceeds on the `001-omnisrouter` feature line. |
| **IX. Ask, Then Wait** | PASS | Owner-gated decisions (create/push public GitHub remote) are held pending explicit approval, not self-resolved. |
| **X. Production Changes Wait for a Human** | N/A (v1) | No production environment yet; when hosted mode arrives, deploys/migrations fall under this gate. Local/dev self-host is exempt. |
| **XI. Establish the Mechanism Before Changing Code** | PASS | Cross-format behavior, embedder choice, and .NET streaming patterns were established against current provider/framework docs (research.md) before design was locked. |
| **XII. Explain Every Number** | PASS | Receipts explain every cost/confidence figure (what it is, why, what follows); cost uses a pinned dated pricing snapshot so figures are reproducible. |

**Result**: No unjustified gate violations. Proceed. (Re-checked after Phase 1 design below — still PASS; see end of Project Structure.)

## Project Structure

### Documentation (this feature)

```text
specs/001-omnisrouter/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output (decisions R1–R4)
├── data-model.md        # Phase 1 output (entities)
├── quickstart.md        # Phase 1 output (validation guide)
├── contracts/           # Phase 1 output (API + internal contracts)
│   ├── README.md
│   ├── wire-formats.md
│   ├── routing-receipt.schema.json
│   ├── route-endpoint.md
│   ├── analytics-decisions.md
│   └── internal-interfaces.md
└── checklists/
    └── requirements.md  # spec quality checklist (passed)
```

### Source Code (repository root)

Single ASP.NET Core service plus supporting projects, mirroring design spec §8:

```text
OmnisRouter.slnx
LICENSE                              # Apache-2.0
README.md
src/
├── OmnisRouter.Api/                 # Kestrel host, minimal-API endpoints, DI, receipt middleware, /ui
├── OmnisRouter.Core/                # Neutral ChatRequest/Response, IRoutingPolicy, ModelDecision, guardrails
├── OmnisRouter.Adapters/            # anthropic/ openai/ gemini/ — ingress+egress format translation
├── OmnisRouter.Routing/             # IEmbedder (ONNX), ClusterScorerPolicy, confidence, session pinning
├── OmnisRouter.Upstream/            # per-provider clients, BYOK (ISecretCipher/IMasterKeyProvider), streaming
├── OmnisRouter.Store/               # EF Core, SQLite/Postgres, encrypted columns
├── OmnisRouter.Telemetry/           # OTLP traces/metrics
└── OmnisRouter.RoutingModel.Build/  # offline reproducible centroids + policy-table build job (CLI)
routing/                             # centroids-<ver>.bin, policy-<ver>.json, build docs (published in-repo)
config/                              # models.yaml, pricing/<date>.yaml
installer/                           # npx thin installer (Claude Code/Codex/Cursor wiring)
deploy/                              # Dockerfile, docker-compose.yml
ui/                                  # self-host dashboard (spend, savings, decision log)
tests/
├── OmnisRouter.Adapters.Tests/      # golden-file conformance + cross-format round-trips
├── OmnisRouter.Routing.Tests/       # deterministic-embedder unit tests + golden routing-model test
├── OmnisRouter.Upstream.Tests/      # BYOK/security + streaming integration (mock upstream)
└── OmnisRouter.Api.Tests/           # endpoint/receipt/integration tests
```

**Structure Decision**: Multi-project .NET solution (design spec §8). The project split is the unit of modularity (Principle I) and independent testability (Principle III): adapters, routing, upstream, and store each have an isolated test project. The offline routing-model builder is a separate CLI project (`OmnisRouter.RoutingModel.Build`) so the reproducible build job ships in-repo (FR-006) without pulling model-training concerns into the request-path service.

**Post-Phase-1 Constitution re-check**: PASS — the Phase 1 contracts and data model introduce no new violations; the receipt schema is pinned as a versioned contract (II/XII), and internal interfaces preserve the modular boundaries (I).

## Complexity Tracking

> Only genuine deviations from "simplest thing" are listed; each is justified.

| Choice | Why needed | Simpler alternative rejected because |
|---|---|---|
| Multi-project solution (8 src + 4 test projects) | Provider adapters, routing, upstream, and store must be independently testable and independently versionable; conformance suite targets adapters in isolation | A single project would entangle provider quirks with routing/storage and make the cross-format conformance suite (the highest-risk area) impossible to target cleanly |
| Separate offline routing-model build CLI | Reproducibility (FR-006) requires a documented, re-runnable job consuming public datasets + benchmark results | Building the model inside the request-path service would couple training-time concerns to runtime and bloat the single-file binary |
| Per-provider EF migrations assemblies | SQLite and Postgres column types differ; a single migration set won't apply to both | One shared migration set fails on the non-authoring provider |
| Two-source session key (header + derived HMAC) | Heterogeneous clients: some send a session header, raw OpenAI-compat/curl clients don't; single-source silently loses cache-warming for part of traffic | Header-only drops pinning for headerless clients; hash-only breaks when a client edits its first message on retry |
