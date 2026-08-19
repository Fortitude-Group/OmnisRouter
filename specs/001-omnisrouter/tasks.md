---
description: "Task list for OmnisRouter v1 implementation"
---

# Tasks: OmnisRouter v1 — Transparent LLM Routing Proxy

**Input**: Design documents from `specs/001-omnisrouter/` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)

**Tests**: INCLUDED. Constitution Principle III mandates comprehensive coverage for public contracts at merge (test-first optional). Adapter conformance + cross-format translation is the highest-risk area (design §10) and gets the most tests.

**Organization**: Grouped by user story (priority order from spec.md) so each story is an independently testable increment.

> **Revised after `/speckit-analyze` (2026-08-19)**: added T020 (seed routing model — unblocks the MVP), T057/T058 (egress + self-host verification tests), and normalized the receipt field to `policy_version`. Tasks renumbered sequentially.

## Format: `[ID] [P?] [Story] Description`

- **[P]** = parallelizable (different files, no dependency on an incomplete task)
- **[Story]** = US1..US5 (setup/foundational/polish carry no story label)
- Every task names an exact path. Paths follow the multi-project `.slnx` layout in plan.md.

## Fan-out map (per always-parallelize rule)

Independent tracks that run concurrently once **Phase 2 (Foundational)** completes:
- **Track A — Adapters** (`OmnisRouter.Adapters`): OpenAI, then Anthropic + Gemini are mutually independent (`[P]`).
- **Track B — Routing** (`OmnisRouter.Routing`): embedder, cluster-scorer, session pinning.
- **Track C — Upstream/BYOK** (`OmnisRouter.Upstream`): provider clients + cipher.
- **Track D — Store/Telemetry** (`OmnisRouter.Store`, `.Telemetry`): decision log, usage, OTLP.
Genuine dependency chains (stay serial): domain model → interfaces → everything; embedder → cluster-scorer → routed endpoint; cipher → ProviderKey column → upstream call. These are called out in Dependencies.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Create `OmnisRouter.slnx` and the 8 src + 4 test + 2 migrations project skeletons per plan.md structure (`src/OmnisRouter.{Api,Core,Adapters,Routing,Upstream,Store,Telemetry,RoutingModel.Build}`, `src/OmnisRouter.Store.Migrations.{Sqlite,Npgsql}`, `tests/OmnisRouter.{Adapters,Routing,Upstream,Api}.Tests`)
- [X] T002 [P] Add `LICENSE` (Apache-2.0) and `README.md` (open-core positioning, quickstart pointer) at repo root
- [X] T003 [P] Add `.editorconfig`, `Directory.Build.props` (nullable enable, warnings-as-errors, `net10.0`), and `dotnet format` config
- [X] T004 [P] Add GitHub Actions CI in `.github/workflows/ci.yml`: restore/build (0 warnings) + `dotnet test`; no live provider keys (fixtures only)
- [X] T005 [P] Seed `config/models.yaml` (candidate pool: provider, model id, capability flags) and `config/pricing/2026-08-15.yaml` (pinned pricing snapshot) — align with OmnisBench cost math
- [X] T006 [P] Add `routing/README.md` documenting the versioned `centroids-<ver>.bin` + `policy-<ver>.json` artifact contract (FR-006)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user-story work begins until this phase is complete.**

### Domain model & interfaces (serial chain — everything depends on these)

- [X] T007 Implement the neutral domain model in `src/OmnisRouter.Core/Model/` — `ChatRequest`, `ChatResponse`, part types (`TextPart`, `ImagePart`, `ToolUsePart`, `ToolResultPart`, `ThinkingPart`), `Tool`, `ToolChoice`, `CacheDirective`, `Usage`, `NeutralStreamEvent` (per data-model.md)
- [X] T008 Implement `ModelRef` + capability flags and `CapabilitiesUsed` derivation in `src/OmnisRouter.Core/Model/ModelRef.cs` and `.../Capabilities.cs`
- [X] T009 Implement `ModelDecision`/receipt record + enums (`Decision`, `Reason`) in `src/OmnisRouter.Core/Routing/ModelDecision.cs` (matches routing-receipt.schema.json; `policy_version` field)
- [X] T010 Define internal interfaces in `src/OmnisRouter.Core/Abstractions/` — `IFormatAdapter`, `IRoutingPolicy`, `IEmbedder`, `ICapabilityGuard`, `ISessionPinner`, `IUpstreamClient`, `ISecretCipher`, `IMasterKeyProvider`, `IDecisionLog`, `IPricingBook` (signatures per contracts/internal-interfaces.md)

### Cross-cutting foundation (parallel once interfaces exist)

- [X] T011 [P] Implement AES-256-GCM `ISecretCipher` + `LocalFileMasterKeyProvider` in `src/OmnisRouter.Upstream/Security/` (fresh 12-byte nonce/write; blob = `keyVersion‖nonce‖tag‖ciphertext`)
- [X] T012 [P] Implement EF Core `OmnisRouterDbContext` + entities (`Install`, `ProviderKey`, `RouterToken`, `Usage`, `DecisionLogEntry`, `SessionPin`) in `src/OmnisRouter.Store/`; encrypted-column `ValueConverter` closing over `ISecretCipher`
- [X] T013 [P] Wire per-provider migrations assemblies `OmnisRouter.Store.Migrations.Sqlite` + `.Npgsql`; startup provider selection by config (SQLite default)
- [X] T014 [P] Implement `IPricingBook` loading `config/pricing/<date>.yaml` in `src/OmnisRouter.Store/Pricing/PricingBook.cs`
- [X] T015 [P] Implement OTLP traces/metrics wiring in `src/OmnisRouter.Telemetry/` (no prompt/key data in spans)
- [X] T016 Kestrel host + DI composition root + config binding + global error middleware in `src/OmnisRouter.Api/Program.cs` and `.../Middleware/ErrorMapping.cs` (maps to each client format's error shape)
- [X] T017 Router-token auth middleware (`Authorization: Bearer`, hashed at rest) in `src/OmnisRouter.Api/Auth/`
- [X] T018 [P] `GET /health` + `GET /readyz` probes in `src/OmnisRouter.Api/Endpoints/Health.cs`
- [X] T019 [P] Test fixtures scaffold: recorded upstream response fixtures + deterministic embedder stub in `tests/OmnisRouter.Adapters.Tests/Fixtures/` and `tests/OmnisRouter.Routing.Tests/Stubs/`
- [X] T020 Produce and commit a **seed routing model** (small `k`, hand/quick-built `centroids-seed.bin` + `policy-seed.json` over the candidate pool) into `routing/`, via a minimal script in `src/OmnisRouter.RoutingModel.Build/seed/` — so US1 can route end-to-end before the full reproducible build (T065/T066) exists. The seed is superseded by the reproducible model in Phase 7.

**Checkpoint**: Foundation ready — user stories can proceed in parallel.

---

## Phase 3: User Story 1 — Drop-in cost routing (Priority: P1) 🎯 MVP

**Goal**: A client swaps only its base URL + key and gets requests routed to the cheapest capable model, returned in its original format. MVP scope: OpenAI Chat Completions format + a single provider's cheap/strong model pair (no cross-provider yet).

**Independent Test**: Point an OpenAI-style client at the router; simple prompts hit the cheaper model, hard prompts the stronger/escalated one; aggregate spend < always-big.

### Tests for US1

- [ ] T021 [P] [US1] Golden-file round-trip conformance for OpenAI ingress/egress (non-stream + stream) in `tests/OmnisRouter.Adapters.Tests/OpenAiConformanceTests.cs`
- [ ] T022 [P] [US1] Routing unit tests (deterministic embedder stub → assert cluster, decision, escalation on low confidence, `policy_version` stamped) in `tests/OmnisRouter.Routing.Tests/ClusterScorerTests.cs`
- [ ] T023 [P] [US1] End-to-end integration through a mock OpenAI upstream (simple vs hard prompt → cheap vs strong) in `tests/OmnisRouter.Api.Tests/RouteOpenAiEndToEndTests.cs`

### Implementation for US1

- [ ] T024 [P] [US1] ONNX `IEmbedder` (bge-small-en-v1.5 int8) + `Microsoft.ML.Tokenizers.BertTokenizer` in `src/OmnisRouter.Routing/Embedding/OnnxEmbedder.cs`
- [ ] T025 [US1] Routing-model loader (`centroids-<ver>.bin` + `policy-<ver>.json`, incl. the seed from T020) in `src/OmnisRouter.Routing/Model/RoutingModelLoader.cs` (depends on T024 dim check + T020 artifact)
- [ ] T026 [US1] `ClusterScorerPolicy` in `src/OmnisRouter.Routing/ClusterScorerPolicy.cs` — nearest centroid (cosine) → softmax confidence → policy-table lookup (cheapest within band) → floor gate/escalation (depends on T025)
- [ ] T027 [P] [US1] OpenAI ingress→internal + internal→OpenAI egress adapter in `src/OmnisRouter.Adapters/OpenAI/OpenAiAdapter.cs`
- [ ] T028 [P] [US1] OpenAI streaming re-frame (internal events → `chat.completion.chunk` SSE + `[DONE]`) in `src/OmnisRouter.Adapters/OpenAI/OpenAiStream.cs`
- [ ] T029 [US1] `IUpstreamClient` for the MVP provider (typed HttpClient, `SocketsHttpHandler`, `InfiniteTimeSpan`, `ResponseHeadersRead`; no retry on stream path) in `src/OmnisRouter.Upstream/Providers/` (depends on T011 cipher for key decrypt)
- [ ] T030 [US1] `POST /v1/chat/completions` routed endpoint (normalize → route → dispatch → translate back, streaming-aware) in `src/OmnisRouter.Api/Endpoints/ChatCompletions.cs` (depends on T026, T027, T029)
- [ ] T031 [US1] `GET /v1/models` advertising the candidate pool in `src/OmnisRouter.Api/Endpoints/Models.cs`
- [ ] T032 [US1] Minimal receipt headers (`X-Omnis-Model/Confidence/Decision`) on the routed response in `src/OmnisRouter.Api/Middleware/ReceiptHeaders.cs`

**Checkpoint**: US1 fully functional — routing + savings demonstrable end-to-end for one format.

---

## Phase 4: User Story 2 — Routing receipts & transparency (Priority: P2)

**Goal**: Full per-response receipts, a cost-free decision-only endpoint, and an exportable decision log.

**Independent Test**: routed responses carry the full receipt; `/v1/route` returns a decision with zero upstream calls; export lists every routed request, content-free.

### Tests for US2

- [ ] T033 [P] [US2] `/v1/route` returns full decision with **zero upstream calls** (assert no egress) in `tests/OmnisRouter.Api.Tests/RouteDecisionOnlyTests.cs`
- [ ] T034 [P] [US2] Decision-log export contains every routed request and **no prompt/key content** in `tests/OmnisRouter.Api.Tests/DecisionLogExportTests.cs`
- [ ] T035 [P] [US2] Receipt schema validation (response matches routing-receipt.schema.json, `policy_version` present) in `tests/OmnisRouter.Api.Tests/ReceiptSchemaTests.cs`

### Implementation for US2

- [ ] T036 [US2] Full receipt set (all `X-Omnis-*` headers incl. cluster/policy/cost/cost-delta/session-pin) in `src/OmnisRouter.Api/Middleware/ReceiptHeaders.cs` (extends T032)
- [ ] T037 [US2] `POST /v1/route` decision-only endpoint (full `ModelDecision` body, no dispatch) in `src/OmnisRouter.Api/Endpoints/Route.cs`
- [ ] T038 [P] [US2] `IDecisionLog` append (content-free `request_hash`, cost/decision fields, outcome/latency) in `src/OmnisRouter.Store/Logging/DecisionLog.cs`
- [ ] T039 [US2] `GET /v1/analytics/routing-decisions` NDJSON streaming export (filters + cursor) in `src/OmnisRouter.Api/Endpoints/AnalyticsDecisions.cs` (depends on T038)
- [ ] T040 [P] [US2] Cost-math wiring: `est_cost_usd` + `est_cost_delta_vs_big_usd` from `IPricingBook` into the decision (Principle XII) in `src/OmnisRouter.Routing/Cost/CostAnnotator.cs`

**Checkpoint**: US1 + US2 both independently functional.

---

## Phase 5: User Story 4 — Feature preservation across formats & providers (Priority: P2)

**Goal**: Add Anthropic + Gemini formats, cross-format streaming/tools/vision/cache/thinking preservation, capability guardrails, and session pinning.

**Independent Test**: streaming/tool/vision requests in every format routed cross-provider return correctly; guardrail returns explicit 400 on a droppable capability; multi-turn pins to keep cache warm.

### Tests for US4

- [ ] T041 [P] [US4] Cross-format conformance matrix (Anthropic-in→OpenAI-model→Anthropic-out, etc.) incl. streaming SSE re-frame in `tests/OmnisRouter.Adapters.Tests/CrossFormatConformanceTests.cs`
- [ ] T042 [P] [US4] Tool-call + vision + cache_control + thinking mapping golden tests in `tests/OmnisRouter.Adapters.Tests/FeaturePreservationTests.cs`
- [ ] T043 [P] [US4] Capability-guardrail refusal tests (vision→non-vision, thinking-signature→different model, parallel-same-tool→Gemini, strict→Gemini) in `tests/OmnisRouter.Routing.Tests/CapabilityGuardTests.cs`
- [ ] T044 [P] [US4] Session-pinning multi-turn tests (warm-cache pin + un-pin on cluster change) in `tests/OmnisRouter.Routing.Tests/SessionPinTests.cs`
- [ ] T045 [P] [US4] Streaming back-pressure + client-cancellation test in `tests/OmnisRouter.Upstream.Tests/StreamingCancellationTests.cs`

### Implementation for US4

- [ ] T046 [P] [US4] Anthropic adapter (ingress/egress + named-event SSE, block bracketing, `tool_use`/`tool_result`, base64 vision, `cache_control`, `thinking`+signature) in `src/OmnisRouter.Adapters/Anthropic/`
- [ ] T047 [P] [US4] Gemini adapter (ingress/egress + whole-object chunk re-framing, `functionCall` name-correlation, `inline_data`/`file_uri`, `thinkingConfig`) in `src/OmnisRouter.Adapters/Gemini/`
- [ ] T048 [US4] `POST /v1/messages` (Anthropic) + `POST /v1beta/models/{model}:{action}` (Gemini) routed endpoints in `src/OmnisRouter.Api/Endpoints/Messages.cs` and `.../GeminiGenerate.cs`
- [ ] T049 [US4] `ICapabilityGuard` pre-dispatch guardrails (refuse-don't-drop per research.md R2) + `X-Omnis-Capability-Notice` for non-fatal degradations in `src/OmnisRouter.Routing/Guardrails/CapabilityGuard.cs`
- [ ] T050 [US4] Router-side image fetch+re-encode for OpenAI remote-URL→base64/file_uri targets in `src/OmnisRouter.Adapters/Common/ImageMaterializer.cs`
- [ ] T051 [US4] `ISessionPinner` (client `X-Omnis-Session-Id` else HMAC(secret, tenant‖system‖first-user)/128-bit; pin/un-pin on cluster change; reflected in receipt) in `src/OmnisRouter.Routing/Pinning/SessionPinner.cs`
- [ ] T052 [US4] Additional upstream clients (Anthropic, Gemini, OpenRouter) in `src/OmnisRouter.Upstream/Providers/` extending T029
- [ ] T053 [US4] Reasoning-continuity conversation pin (force one model when prior thinking signatures present) wired into `ClusterScorerPolicy` + guard

**Checkpoint**: All three formats + faithful cross-provider preservation working.

---

## Phase 6: User Story 5 — Secure BYOK single-process self-host (Priority: P2)

**Goal**: Encrypted-at-rest BYOK, no key/prompt leakage, single-process/embedded-datastore self-host, packaged.

**Independent Test**: key ciphertext-only at rest; no key/prompt in logs/receipts/exports; prompt egress only to the chosen upstream; runs with no external dependency.

### Tests for US5

- [ ] T054 [P] [US5] Encryption-at-rest test (ProviderKey stored ciphertext; round-trip decrypt; nonce uniqueness) in `tests/OmnisRouter.Upstream.Tests/ByokCipherTests.cs`
- [ ] T055 [P] [US5] Leakage scan test (keys + prompt text absent from logs, receipts, NDJSON export) in `tests/OmnisRouter.Api.Tests/NoLeakageTests.cs`
- [ ] T056 [P] [US5] No-fallback-on-missing-key test (explicit non-leaking error) in `tests/OmnisRouter.Upstream.Tests/MissingKeyTests.cs`
- [ ] T057 [P] [US5] **Network-egress restriction test** (mock DNS/handler; assert prompt content leaves only to the chosen provider host and to no other destination — FR-013/SC-007) in `tests/OmnisRouter.Upstream.Tests/EgressRestrictionTests.cs`
- [ ] T058 [P] [US5] **Self-host smoke test** (boot with SQLite + no external service; route one request end-to-end — FR-015/SC-008) in `tests/OmnisRouter.Api.Tests/SelfHostSmokeTests.cs`

### Implementation for US5

- [ ] T059 [P] [US5] BYOK key management endpoints/CLI (add/rotate/revoke provider keys; `keyVersion` rotation) in `src/OmnisRouter.Api/Endpoints/Keys.cs`
- [ ] T060 [P] [US5] Log/receipt redaction filter guaranteeing no secret/prompt egress in `src/OmnisRouter.Telemetry/Redaction/`
- [ ] T061 [P] [US5] Single-file `dotnet publish` profile + `deploy/Dockerfile` + `deploy/docker-compose.yml` (SQLite volume)
- [ ] T062 [US5] Self-host config docs (env, key file location/permissions, Postgres opt-in) in `docs/self-host.md`

**Checkpoint**: US1/2/4/5 functional; product usable and secure for self-host.

---

## Phase 7: User Story 3 — Reproducible, verifiable routing (Priority: P3)

**Goal**: Offline reproducible routing-model build + published versioned artifacts (superseding the T020 seed) + determinism guard + benchmark release gate.

**Independent Test**: rebuild from public data reproduces the shipped model within tolerance; tagged release publishes its OmnisBench frontier; golden routing-model decisions stable.

### Tests for US3

- [ ] T063 [P] [US3] Golden routing-model test (committed centroids/policy + fixed inputs → stable decisions) in `tests/OmnisRouter.Routing.Tests/GoldenRoutingModelTests.cs` (depends on T066 shipped model + T025 loader)
- [ ] T064 [P] [US3] Build-reproducibility test (same inputs → decision-equivalent model within tolerance) in `tests/OmnisRouter.Routing.Tests/BuildReproducibilityTests.cs`

### Implementation for US3

- [ ] T065 [US3] Offline build CLI `OmnisRouter.RoutingModel.Build` (`build-model --datasets --bench-results --k --epsilon --min-samples --out`) in `src/OmnisRouter.RoutingModel.Build/` — embed→k-means centroids→per-cluster policy table (relative band ε, rank by cost, n≥30, `low_confidence` flag)
- [ ] T066 [US3] Emit versioned `centroids-<ver>.bin` + `policy-<ver>.json` + build manifest into `routing/`; commit the shipped v1 model (supersedes the T020 seed)
- [ ] T067 [P] [US3] Build-job docs (rebuild-it-yourself steps, public datasets, OmnisBench inputs) in `routing/BUILD.md`
- [ ] T068 [US3] Release gate script: block tag unless clean build + green tests + passing OmnisBench run; publish frontier with the release (FR-018) in `scripts/release-gate.ps1`
- [ ] T069 [P] [US3] Thin `npx` installer (Claude Code plugin / Codex config / Cursor base URL wiring) in `installer/`

**Checkpoint**: All user stories independently functional; savings claims verifiable.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T070 [P] Self-host dashboard `GET /ui` (spend, savings, recent decisions table) in `ui/` + `src/OmnisRouter.Api/Endpoints/Ui.cs`
- [ ] T071 Performance pass: verify ≤50ms p95 routing overhead (embed-path benchmark, per-session embedding cache) — `tests/OmnisRouter.Routing.Tests/LatencyBenchmark.cs` + fixes (SC-003)
- [ ] T072 [P] README + `docs/` finalization (API surface, receipts, transparency claims), and `docs/api.md`
- [ ] T073 [P] Confidence calibration pass: fit temperature `T` + floor `τ`; run elbow/silhouette to confirm `k` under the n≥30 cap; record chosen values in `routing/BUILD.md`
- [ ] T074 Run `quickstart.md` validation end-to-end and fix gaps
- [ ] T075 [P] Security hardening review (path/token validation, no-leak re-audit) and dependency pin/scan

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup; **blocks all stories**. Within it: T007→T008→T009→T010 is the serial spine; T011–T019 parallelize after T010 (T012/T013 depend on T011 cipher; T016/T017 depend on interfaces). T020 (seed model) depends on the candidate pool (T005) and the build project skeleton (T001); it can run in parallel with T011–T019.
- **US1 (P1)** → after Foundational. Serial spine: T024→T025→T026; T025 also needs the T020 seed artifact; T030 depends on T026+T027+T029.
- **US2, US4, US5 (P2)** → each after Foundational; US2 depends on US1's endpoint/receipt seam (T032). US4/US5 are largely independent of US2 and of each other (different files/tracks) → run in parallel.
- **US3 (P3)** → after Foundational; the build CLI (T065) is independent, but T063 golden test needs the shipped model (T066) and the loader (T025).
- **Polish (P8)** → after target stories complete.

### Parallel opportunities
- All `[P]` Setup tasks (T002–T006) together.
- After T010: T011, T012(+T013), T014, T015, T018, T019, T020 in parallel.
- After Foundational: **Track A (adapters)**, **Track B (routing)**, **Track C (upstream/BYOK)**, **Track D (store/telemetry)** proceed concurrently. US4's Anthropic (T046) and Gemini (T047) adapters are `[P]` to each other.
- All test tasks within a story marked `[P]` run together (and, per Constitution III, can be authored before or alongside implementation).

### Serial (genuine chains — do NOT parallelize)
- Domain model → interfaces → all implementations.
- Seed model (T020) → routing-model loader (T025) → cluster-scorer (T026) → routed endpoints.
- Embedder (T024) → loader (T025).
- Cipher (T011) → ProviderKey encrypted column (T012) → upstream key decrypt (T029) → any real routed call.

---

## Parallel Example: post-Foundational fan-out

```text
# Four concurrent tracks (separate projects, no shared files):
Track A: T027 OpenAI adapter        (OmnisRouter.Adapters/OpenAI)
Track B: T024 ONNX embedder         (OmnisRouter.Routing/Embedding)
Track C: T029 upstream client       (OmnisRouter.Upstream/Providers)
Track D: T038 decision log          (OmnisRouter.Store/Logging)
```

---

## Implementation Strategy

- **MVP = Phases 1+2+3 (US1)**: setup → foundation (incl. T020 seed routing model) → drop-in OpenAI routing. STOP and validate savings before proceeding.
- **Increment**: add US2 (receipts) → US4 (all formats + preservation) → US5 (secure self-host) → US3 (reproducibility + release gate, supersedes the seed model) → polish.
- **Execution mechanism**: fan the independent tracks out via Claude Flow/RuFlo (or the Task-tool fallback), one agent per track, with per-task review; keep the serial spines single-agent. Move each task to done as it lands (Constitution VII).
- **Release**: no tag without clean build + green tests + a passing OmnisBench run (FR-018 / SC-009).

## Notes
- `[P]` = different files, no incomplete-task dependency.
- Tests are required for merge (Constitution III), not necessarily test-first.
- Commit after each task or logical group; keep provider fixtures recorded (no live keys in CI).
